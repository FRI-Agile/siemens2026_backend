-- ============================================================================
--  Database schema (DDL)
--  Generated from docs/db/README.md and its module documents  (schema version 0.1.x)
--
--  Target: PostgreSQL 18+  (uses old-row values in RETURNING, native uuidv7()).
--  Mounted into /docker-entrypoint-initdb.d by the Aspire AppHost. It runs once, against
--  the fresh database the container creates (POSTGRES_DB), so it does not CREATE DATABASE
--  itself. The local database has no volume, so every AppHost run starts from this script.
--
--  This script is the schema (ADR-D002 in docs/db/decision-log.md): edit it in place and
--  bump the documentation's schema version in the same change. The EF Core contexts map
--  their tables with ExcludeFromMigrations.
-- ============================================================================

-- ---------------------------------------------------------------------------
-- Schemas (one per module / bounded context, plus the host-owned kernel)
-- ---------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS kernel;
CREATE SCHEMA IF NOT EXISTS recruitment;

-- ---------------------------------------------------------------------------
-- Extensions (database-wide; created by the operator in Azure, ADR-D006)
-- ---------------------------------------------------------------------------
-- pg_trgm: the trigram GIN indexes behind free-text search (conventions.md § Search indexes). In `public`, so every
-- module schema reaches it the same way; its operator classes are always written schema-qualified.
CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;
-- unaccent: accent-insensitive search, through each module's immutable_unaccent wrapper (ADR-D007).
CREATE EXTENSION IF NOT EXISTS unaccent WITH SCHEMA public;


-- ===========================================================================
--  SHARED: kernel
-- ===========================================================================

-- idempotency  (HTTP Idempotency-Key records, ADR-A016 / idempotency middleware)
-- One row per claimed idempotency key. `completed = false` is a claim in flight; `true` carries the
-- captured response, replayed verbatim until `expires_at`.
--
-- A schema of its own rather than a module's: a claim is taken by host middleware *before* routing has
-- chosen a handler, so there is no module to attribute it to (ADR-D001).
--
-- `lease_expires_at` is what makes a durable store safe. A process that dies mid-request never completes
-- or abandons its claim; without a takeover window every retry of that key would be answered 409 until the
-- full TTL elapsed. The in-memory store needs no lease only because its state dies with the process that
-- stranded it. The lease must exceed the slowest covered endpoint — if it expires while the work is still
-- running, a duplicate gets in.
--
-- `claim_owner` is what distinguishes "my own attempt, retried" from "somebody else's claim". The API's
-- contexts retry on transient failures, so a claim whose INSERT committed but whose acknowledgement was
-- lost gets re-sent; without an owner the store would answer that retry with 409 — telling a request that
-- the claim it owns belongs to someone else. The owner is per request (the store is scoped), so any
-- second look at a claim carrying the same owner is the same attempt.
--
-- It guards completion and release as well, not just the claim. A request slow enough to lose its lease is
-- still running and will still finish, but the key is no longer its own: completing would overwrite the
-- winner's captured response with the loser's, and releasing would delete a live claim and let a third
-- request start alongside the second.
--
-- Churn-heavy by design: insert, one update, delete within the TTL, at the rate of covered writes. That
-- produces dead tuples far faster than the row count suggests, hence the per-table autovacuum settings.
CREATE TABLE kernel.idempotency (
    key              TEXT         PRIMARY KEY,
    fingerprint      BYTEA        NOT NULL,  -- SHA-256 over method + path + query + body
    claim_owner      UUID         NOT NULL,  -- which request holds the claim; see the note on retries above
    completed        BOOLEAN      NOT NULL DEFAULT false,
    lease_expires_at TIMESTAMPTZ  NOT NULL,  -- while uncompleted: when another request may take the claim over
    expires_at       TIMESTAMPTZ  NOT NULL,  -- when the record stops being replayable and may be pruned
    status_code      INTEGER,                -- captured response, null while uncompleted
    content_type     TEXT,
    location         TEXT,
    body             BYTEA,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT now(),
    CONSTRAINT idempotency_completed_has_status CHECK (completed = false OR status_code IS NOT NULL)
);
-- Makes the retention pass a cheap range delete instead of a full-table scan.
CREATE INDEX idx_idempotency_expires_at ON kernel.idempotency (expires_at);
-- The default 20% dead-tuple threshold lets a churn table like this bloat between passes.
ALTER TABLE kernel.idempotency SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.02);


-- ===========================================================================
--  MODULE: recruitment
-- ===========================================================================

-- set_audit_fields  (the audit trigger, conventions.md § Audit trigger)
-- Maintains `updated_at` and `version` on every update, so the application can never forget them. A copy per
-- module schema rather than one shared function, so a module's schema stays self-contained when it is
-- extracted (ADR-D003).
CREATE OR REPLACE FUNCTION recruitment.set_audit_fields()
RETURNS trigger AS $$
BEGIN
    NEW.updated_at := now();
    NEW.version    := OLD.version + 1;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- immutable_unaccent  (accent-insensitive search, ADR-D007)
-- unaccent() is only STABLE, as it finds its dictionary through search_path, so an index cannot use it. This wrapper
-- names the function and dictionary by schema and is declared IMMUTABLE, so a trigram index can store the unaccented
-- text. A search compares immutable_unaccent(column) with immutable_unaccent(term). In the module's schema like the
-- audit trigger (ADR-D003), where the application role may create it. Editing the unaccent rules file means
-- rebuilding the indexes that use it (REINDEX).
CREATE OR REPLACE FUNCTION recruitment.immutable_unaccent(text)
RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
RETURN public.unaccent('public.unaccent'::regdictionary, $1);

-- job_post  (a position the company is hiring for)
-- Created by a hiring manager. `status` is `draft` (koncept, seen by staff only), `published` (visible to applicants)
-- or `closed` (no longer accepting applicants, hidden from them); `draft -> published <-> closed`.
--
-- `recruiter_id` is the Entra object id (`oid`) of the staff member with the Recruiter role who is responsible
-- for the hiring (zodpovedný za realizáciu náboru), chosen by the hiring manager who creates the post. Staff
-- accounts live in Entra, not in this database, so it is a logical reference with no foreign key (ADR-D004); the
-- application checks the role on write.
CREATE TABLE recruitment.job_post (
    id               UUID           PRIMARY KEY DEFAULT uuidv7(),
    title            TEXT           NOT NULL,
    location         TEXT           NOT NULL,
    department       TEXT           NOT NULL,
    description      TEXT           NOT NULL,
    status           TEXT           NOT NULL DEFAULT 'draft',  -- draft / published / closed
    open_positions   INTEGER        NOT NULL,
    salary           NUMERIC(10, 2) NOT NULL,  -- EUR, per `payment_type`
    payment_type     TEXT           NOT NULL,  -- hourly / monthly / yearly wage
    employment_type  TEXT           NOT NULL,  -- typ pracovného pomeru: TPP, part-time, work agreements, ...
    recruiter_id     UUID           NOT NULL,  -- Entra oid of the responsible recruiter; logical, no FK
    created_at       TIMESTAMPTZ    NOT NULL DEFAULT now(),
    created_by       UUID           NOT NULL,
    updated_at       TIMESTAMPTZ,
    updated_by       UUID,
    version          SMALLINT       NOT NULL DEFAULT 1,
    CONSTRAINT job_post_title_valid CHECK (btrim(title) <> ''),
    CONSTRAINT job_post_location_valid CHECK (btrim(location) <> ''),
    CONSTRAINT job_post_department_valid CHECK (btrim(department) <> ''),
    CONSTRAINT job_post_description_valid CHECK (btrim(description) <> ''),
    CONSTRAINT job_post_status_valid CHECK (status IN ('draft', 'published', 'closed')),
    CONSTRAINT job_post_open_positions_valid CHECK (open_positions > 0),
    CONSTRAINT job_post_salary_valid CHECK (salary > 0),
    CONSTRAINT job_post_payment_type_valid CHECK (payment_type IN ('hourly', 'monthly', 'yearly')),
    CONSTRAINT job_post_employment_type_valid CHECK (employment_type IN (
        'full_time', 'part_time', 'fixed_term', 'work_performance_agreement', 'work_activity_agreement',
        'student_work_agreement', 'self_employed'))
);
-- A recruiter's own job posts.
CREATE INDEX idx_job_post_recruiter_id ON recruitment.job_post (recruiter_id);
-- The job post lists' search, `title ILIKE '%…%' OR location ILIKE '%…%'`: one trigram index per column, which the
-- planner combines for the OR. A b-tree cannot serve a pattern with a leading wildcard.
CREATE INDEX idx_job_post_title ON recruitment.job_post USING gin (title public.gin_trgm_ops);
CREATE INDEX idx_job_post_location ON recruitment.job_post USING gin (location public.gin_trgm_ops);
CREATE TRIGGER trg_job_post_set_audit
    BEFORE UPDATE ON recruitment.job_post
    FOR EACH ROW EXECUTE FUNCTION recruitment.set_audit_fields();

-- candidate  (a person who applied to at least one job post)
-- Applicants are anonymous (ADD §2), so a row is created by the first application carrying its e-mail and has no
-- `created_by` (ADR-D008); staff may correct it later, hence `updated_by`. One row per e-mail, compared without case:
-- a later application with a known e-mail is linked to the existing row and does not change it, as nothing proves
-- the applicant owns the address.
--
-- `display_name` is generated from the two name parts, so the name search has one column to index and match
-- ("Martin Kaš…" as well as "Kašuba"). `phone_number` is E.164 (`+421905123456`), normalised by the application.
CREATE TABLE recruitment.candidate (
    id            UUID         PRIMARY KEY DEFAULT uuidv7(),
    first_name    TEXT         NOT NULL,
    last_name     TEXT         NOT NULL,
    display_name  TEXT         NOT NULL GENERATED ALWAYS AS (first_name || ' ' || last_name) STORED,
    email         TEXT         NOT NULL,
    phone_number  TEXT         NOT NULL,  -- E.164
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ,
    updated_by    UUID,
    version       SMALLINT     NOT NULL DEFAULT 1,
    CONSTRAINT candidate_first_name_valid CHECK (btrim(first_name) <> ''),
    CONSTRAINT candidate_last_name_valid CHECK (btrim(last_name) <> ''),
    CONSTRAINT candidate_email_valid CHECK (email ~ '^[^@\s]+@[^@\s]+$'),
    CONSTRAINT candidate_phone_number_valid CHECK (phone_number ~ '^\+[1-9][0-9]{6,14}$')
);
-- One candidate per e-mail, regardless of case; also the lookup a new application makes.
CREATE UNIQUE INDEX uq_candidate_email ON recruitment.candidate (lower(email));
-- The candidate search, `immutable_unaccent(display_name) ILIKE immutable_unaccent('%…%') OR` the same on `email`:
-- a trigram index per column over the unaccented text, combined with a BitmapOr like the job post search.
CREATE INDEX idx_candidate_display_name
    ON recruitment.candidate USING gin (recruitment.immutable_unaccent(display_name) public.gin_trgm_ops);
CREATE INDEX idx_candidate_email
    ON recruitment.candidate USING gin (recruitment.immutable_unaccent(email) public.gin_trgm_ops);
CREATE TRIGGER trg_candidate_set_audit
    BEFORE UPDATE ON recruitment.candidate
    FOR EACH ROW EXECUTE FUNCTION recruitment.set_audit_fields();

-- application  (a candidate's application to one job post; žiadosť)
-- Submitted by the anonymous candidate to a published post (checked by the application), so no `created_by`
-- (ADR-D008). Staff then move it through the hiring pipeline: `new -> screening -> interview -> offer -> hired`, and
-- `rejected` from any step before `hired`. A candidate applies to a post once.
--
-- The job post must not disappear under its applications, so RESTRICT; posts are closed, never deleted. An
-- application means nothing without its candidate, so erasing a candidate erases their applications.
CREATE TABLE recruitment.application (
    id            UUID         PRIMARY KEY DEFAULT uuidv7(),
    job_post_id   UUID         NOT NULL,
    candidate_id  UUID         NOT NULL,
    status        TEXT         NOT NULL DEFAULT 'new',  -- new / screening / interview / offer / hired / rejected
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ,
    updated_by    UUID,
    version       SMALLINT     NOT NULL DEFAULT 1,
    CONSTRAINT application_job_post_fk
        FOREIGN KEY (job_post_id) REFERENCES recruitment.job_post (id) ON DELETE RESTRICT,
    CONSTRAINT application_candidate_fk
        FOREIGN KEY (candidate_id) REFERENCES recruitment.candidate (id) ON DELETE CASCADE,
    CONSTRAINT application_status_valid CHECK (status IN (
        'new', 'screening', 'interview', 'offer', 'hired', 'rejected'))
);
-- One application per candidate and post. Leading with the post, it is also the job post detail's lookup of the
-- post's applications.
CREATE UNIQUE INDEX uq_application_job_post_id_candidate_id
    ON recruitment.application (job_post_id, candidate_id);
-- A candidate's applications, and the FK's index for the cascade from candidate.
CREATE INDEX idx_application_candidate_id ON recruitment.application (candidate_id);
CREATE TRIGGER trg_application_set_audit
    BEFORE UPDATE ON recruitment.application
    FOR EACH ROW EXECUTE FUNCTION recruitment.set_audit_fields();

-- application_document  (a file the candidate submitted with an application: CV, cover letter, …)
-- The file's metadata; the blob is in the module's container, named by `id` rather than by the user's file name
-- (ADR-A010). `status` follows the upload: `uploading` once the upload URL is issued, `scanning` once the candidate
-- confirms it and its size and type check out, `clean` once ClamAV reports so. Staff see `clean` documents only. An
-- infected file is deleted with its row, and so is an upload not confirmed within an hour.
--
-- Written by the anonymous candidate and then only by the system, so neither `created_by` nor `updated_by`
-- (ADR-D008); `version` still guards the status moves against a pruner and a confirmation racing.
CREATE TABLE recruitment.application_document (
    id              UUID         PRIMARY KEY DEFAULT uuidv7(),  -- also the blob's name
    application_id  UUID         NOT NULL,
    type            TEXT         NOT NULL,  -- cv / cover_letter / other
    file_name       TEXT         NOT NULL,  -- as uploaded, shown to staff and used for downloads
    content_type    TEXT         NOT NULL,
    size_bytes      BIGINT       NOT NULL,
    status          TEXT         NOT NULL DEFAULT 'uploading',  -- uploading / scanning / clean
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ,
    version         SMALLINT     NOT NULL DEFAULT 1,
    CONSTRAINT application_document_application_fk
        FOREIGN KEY (application_id) REFERENCES recruitment.application (id) ON DELETE CASCADE,
    CONSTRAINT application_document_type_valid CHECK (type IN ('cv', 'cover_letter', 'other')),
    CONSTRAINT application_document_file_name_valid CHECK (btrim(file_name) <> ''),
    CONSTRAINT application_document_content_type_valid CHECK (btrim(content_type) <> ''),
    CONSTRAINT application_document_size_bytes_valid CHECK (size_bytes > 0),
    CONSTRAINT application_document_status_valid CHECK (status IN ('uploading', 'scanning', 'clean'))
);
-- An application's documents, and the FK's index for the cascade from application.
CREATE INDEX idx_application_document_application_id ON recruitment.application_document (application_id);
-- The pruner's candidates: uploads never confirmed, oldest first. Partial, so it holds only the short-lived backlog.
CREATE INDEX idx_application_document_created_at
    ON recruitment.application_document (created_at) WHERE status = 'uploading';
CREATE TRIGGER trg_application_document_set_audit
    BEFORE UPDATE ON recruitment.application_document
    FOR EACH ROW EXECUTE FUNCTION recruitment.set_audit_fields();
