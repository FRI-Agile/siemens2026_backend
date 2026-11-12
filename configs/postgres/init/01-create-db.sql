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
CREATE SCHEMA IF NOT EXISTS files;
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

-- altcha_solution  (spent ALTCHA proof-of-work solutions, ADR-022)
-- One row per challenge whose solution was accepted, kept until the challenge expires, so a solution is accepted only
-- once. A schema of its own for the same reason as `idempotency`: the check is a host endpoint filter, not a module's.
-- `challenge` is the SHA-256 the signature covers, so moving digits between the salt and the number cannot spend the
-- same challenge twice under a different row. Insert-only churn, deleted by `AltchaSolutionPruner` past `expires_at`.
CREATE TABLE kernel.altcha_solution (
    challenge   TEXT         PRIMARY KEY,  -- hex SHA-256 of salt || number
    expires_at  TIMESTAMPTZ  NOT NULL,     -- the challenge's own expiry; the row may be pruned after it
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE INDEX idx_altcha_solution_expires_at ON kernel.altcha_solution (expires_at);
ALTER TABLE kernel.altcha_solution SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.02);


-- ===========================================================================
--  MODULE: files
-- ===========================================================================

-- file  (one uploaded object's metadata; docs/api/files/files.md)
-- The bytes are in object storage under `object_key`, server-generated and never the client's file name. Other modules
-- reference a file by `id` with no foreign key (a logical reference, conventions.md § Cross-module references); the
-- owning module confirms and releases it through the Files module as the reference comes and goes.
--
-- No status column: a file is pending while `etag` is NULL (the upload URL is issued, nothing references it yet),
-- confirmed once a row references it and its object checked out (`etag`, the real `size_bytes`), clean once ClamAV
-- cleared it (`scanned_at`), and released once `deleted_at` is set. Only a confirmed, clean, live file is downloadable.
-- The janitor reaps a file still pending after its upload URL expired and purges a released one after 7 days.
--
-- Uploaded by an anonymous applicant or a staff member, then written only by the system, so no `version` and no audit
-- trigger; `uploaded_at` / `uploaded_by` stand for `created_at` / `created_by` (ADR-D008).
CREATE TABLE files.file (
    id            UUID         PRIMARY KEY DEFAULT uuidv7(),
    purpose       TEXT         NOT NULL,  -- IFilePurpose.Key of the module that declared the kind, e.g. application_document
    object_key    TEXT         NOT NULL,  -- uploads/{id}.ext in the module's bucket / container
    file_name     TEXT         NOT NULL,  -- as uploaded, shown to staff and used for downloads
    content_type  TEXT         NOT NULL,
    size_bytes    BIGINT       NOT NULL,  -- declared until confirmed, then the object's real size
    etag          TEXT,                   -- NULL while pending
    scanned_at    TIMESTAMPTZ,            -- when ClamAV found it clean; NULL until then
    uploaded_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    uploaded_by   UUID,                   -- staff Entra oid; NULL for an anonymous applicant
    deleted_at    TIMESTAMPTZ,            -- released (soft delete)
    deleted_by    UUID,                   -- staff Entra oid whose change released it; NULL for the system or an applicant
    CONSTRAINT file_purpose_valid CHECK (btrim(purpose) <> ''),
    CONSTRAINT file_object_key_valid CHECK (btrim(object_key) <> ''),
    CONSTRAINT file_file_name_valid CHECK (btrim(file_name) <> ''),
    CONSTRAINT file_content_type_valid CHECK (btrim(content_type) <> ''),
    CONSTRAINT file_size_bytes_valid CHECK (size_bytes > 0),
    -- Only a confirmed file is scanned.
    CONSTRAINT file_scanned_at_valid CHECK (scanned_at IS NULL OR etag IS NOT NULL)
);
-- One row per stored object.
CREATE UNIQUE INDEX uq_file_object_key ON files.file (object_key);
-- The janitor's reap: pending files whose upload URL expired, oldest first. Partial, so it holds only the short-lived
-- backlog of uploads in flight.
CREATE INDEX idx_file_pending_uploaded_at ON files.file (uploaded_at) WHERE etag IS NULL;
-- The scanner's backlog: confirmed live files ClamAV has not cleared, oldest first.
CREATE INDEX idx_file_unscanned_uploaded_at
    ON files.file (uploaded_at) WHERE etag IS NOT NULL AND scanned_at IS NULL AND deleted_at IS NULL;
-- The janitor's retention sweep: released files, oldest release first.
CREATE INDEX idx_file_deleted_at ON files.file (deleted_at) WHERE deleted_at IS NOT NULL;


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
    hiring_manager_id UUID          NOT NULL,  -- Entra oid of the hiring manager the position is filled for; logical, no FK
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
-- A hiring manager's own job posts.
CREATE INDEX idx_job_post_hiring_manager_id ON recruitment.job_post (hiring_manager_id);
-- The job post lists' search, accent-insensitive and fuzzy on both the title and the location:
-- `immutable_unaccent(col) ILIKE immutable_unaccent('%…%')` or pg_trgm's word similarity
-- (`immutable_unaccent(term) <% immutable_unaccent(col)`), so "kosice" finds "Košice" and a typo still matches. One
-- trigram index per column, which the planner combines for the OR. A b-tree cannot serve a leading wildcard.
CREATE INDEX idx_job_post_title
    ON recruitment.job_post USING gin (recruitment.immutable_unaccent(title) public.gin_trgm_ops);
CREATE INDEX idx_job_post_location
    ON recruitment.job_post USING gin (recruitment.immutable_unaccent(location) public.gin_trgm_ops);
CREATE TRIGGER trg_job_post_set_audit
    BEFORE UPDATE ON recruitment.job_post
    FOR EACH ROW EXECUTE FUNCTION recruitment.set_audit_fields();

-- application  (one person's application to one job post; žiadosť)
-- Each job post has its own applicants: the row carries who applied (name, e-mail, phone) as they filled in the form,
-- and there is no shared candidate table (ADR-D010). Someone applying to two posts is two unrelated rows, each with the
-- details typed for it. Submitted by the anonymous applicant to a published post (checked by the application), so no
-- `created_by` (ADR-D008); staff correct the details and move the status, hence `updated_by`.
--
-- Staff move it through the hiring pipeline: `nová žiadosť -> predvýber -> pohovor -> ponuka -> prijatý`, and
-- `odmietnutý` from any step before `prijatý`. The codes are the Slovak terms as written (ADR-D009).
--
-- `display_name` is generated from the two name parts, so the name search has one column to index and match
-- ("Martin Kaš…" as well as "Kašuba"). `phone_number` is E.164 (`+421905123456`) and optional on the form.
--
-- The job post must not disappear under its applications, so RESTRICT; posts are closed, never deleted.
CREATE TABLE recruitment.application (
    id            UUID         PRIMARY KEY DEFAULT uuidv7(),
    job_post_id   UUID         NOT NULL,
    first_name    TEXT         NOT NULL,
    last_name     TEXT         NOT NULL,
    display_name  TEXT         NOT NULL GENERATED ALWAYS AS (first_name || ' ' || last_name) STORED,
    email         TEXT         NOT NULL,
    phone_number  TEXT,                   -- E.164; optional on the application form
    status        TEXT         NOT NULL DEFAULT 'nová žiadosť',  -- nová žiadosť / predvýber / pohovor / ponuka / prijatý / odmietnutý
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ,
    updated_by    UUID,
    version       SMALLINT     NOT NULL DEFAULT 1,
    CONSTRAINT application_job_post_fk
        FOREIGN KEY (job_post_id) REFERENCES recruitment.job_post (id) ON DELETE RESTRICT,
    CONSTRAINT application_first_name_valid CHECK (btrim(first_name) <> ''),
    CONSTRAINT application_last_name_valid CHECK (btrim(last_name) <> ''),
    CONSTRAINT application_email_valid CHECK (email ~ '^[^@\s]+@[^@\s]+$'),
    CONSTRAINT application_phone_number_valid CHECK (phone_number ~ '^\+[1-9][0-9]{6,14}$'),
    CONSTRAINT application_status_valid CHECK (status IN (
        'nová žiadosť', 'predvýber', 'pohovor', 'ponuka', 'prijatý', 'odmietnutý'))
);
-- One application per e-mail and post, regardless of case: applying again to the same post is refused.
CREATE UNIQUE INDEX uq_application_job_post_id_email ON recruitment.application (job_post_id, lower(email));
-- A post's applicants newest or oldest first, a page at a time (the staff job post detail); read backwards for newest.
CREATE INDEX idx_application_job_post_id_created_at ON recruitment.application (job_post_id, created_at, id);
-- The applicant search. The name is accent-insensitive and fuzzy: `immutable_unaccent(display_name)` matched with
-- `ILIKE immutable_unaccent('%…%')` or pg_trgm's word similarity (`immutable_unaccent(term) <% …`), so "kasuba" and a
-- typo like "kasubo" find "Kašuba". The e-mail is a plain `ILIKE '%…%'`. A trigram index per column, combined with a
-- BitmapOr.
CREATE INDEX idx_application_display_name
    ON recruitment.application USING gin (recruitment.immutable_unaccent(display_name) public.gin_trgm_ops);
CREATE INDEX idx_application_email ON recruitment.application USING gin (email public.gin_trgm_ops);
CREATE TRIGGER trg_application_set_audit
    BEFORE UPDATE ON recruitment.application
    FOR EACH ROW EXECUTE FUNCTION recruitment.set_audit_fields();

-- application_document  (a file the applicant submitted with an application: CV, cover letter, …)
-- What the document is (`type`) and which file it is (`file_id`). The file itself — its name, type, size, bytes and
-- malware scan — belongs to the Files module (`files.file`), referenced logically with no foreign key. The entity is
-- change-captured, so the Files module confirms the file when a row gains it and releases it when the row goes, through
-- this module's outbox (docs/api/files/files.md §9). The codes are the Slovak terms as written (ADR-D009).
--
-- A row deleted by the cascade from `application` is not captured: a feature deleting an application publishes
-- `files_released` with the documents' file ids in the same transaction.
--
-- Written by the anonymous applicant and then only by the system, so neither `created_by` nor `updated_by`
-- (ADR-D008).
CREATE TABLE recruitment.application_document (
    id              UUID         PRIMARY KEY DEFAULT uuidv7(),
    application_id  UUID         NOT NULL,
    type            TEXT         NOT NULL,  -- životopis / motivačný list / iné
    file_id         UUID         NOT NULL,  -- files.file(id); logical, no FK
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ,
    version         SMALLINT     NOT NULL DEFAULT 1,
    CONSTRAINT application_document_application_fk
        FOREIGN KEY (application_id) REFERENCES recruitment.application (id) ON DELETE CASCADE,
    CONSTRAINT application_document_type_valid CHECK (type IN ('životopis', 'motivačný list', 'iné'))
);
-- An application's documents, and the FK's index for the cascade from application.
CREATE INDEX idx_application_document_application_id ON recruitment.application_document (application_id);
-- A file belongs to one reference: two rows naming it would have the first one's release delete it under the second.
CREATE UNIQUE INDEX uq_application_document_file_id ON recruitment.application_document (file_id);
-- At most one CV and one cover letter per application, so "the application's CV" names one row: the staff detail and the
-- AI scoring read it by (application_id, type). Any number of other documents.
CREATE UNIQUE INDEX uq_application_document_application_id_type
    ON recruitment.application_document (application_id, type)
    WHERE type IN ('životopis', 'motivačný list');
CREATE TRIGGER trg_application_document_set_audit
    BEFORE UPDATE ON recruitment.application_document
    FOR EACH ROW EXECUTE FUNCTION recruitment.set_audit_fields();

-- outbox_message  (the module's transactional outbox; conventions.md § Outbox pattern)
-- One row per captured change of a tracked entity (today `application_document`) and per published event, written in
-- the same transaction as the change; the API's dispatcher delivers it to other modules' handlers and deletes it.
-- `attempts` / `next_attempt_at` / `last_error` record failed deliveries: the row is retried with a backoff and left
-- for an operator after 10 attempts.
CREATE TABLE recruitment.outbox_message (
    id               UUID         PRIMARY KEY DEFAULT uuidv7(),
    kind             TEXT         NOT NULL DEFAULT 'row_change',
    module           TEXT         NOT NULL,
    event            TEXT         NOT NULL,
    entity_type      TEXT         NOT NULL,
    entity_id        TEXT         NOT NULL,
    action           TEXT         NOT NULL,
    old_value        JSONB,
    new_value        JSONB,
    actor_type       TEXT         NOT NULL,
    actor_user_id    UUID,
    actor_service    TEXT,
    trace_id         TEXT,
    occurred_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    version          SMALLINT     NOT NULL DEFAULT 0,
    attempts         SMALLINT     NOT NULL DEFAULT 0,
    next_attempt_at  TIMESTAMPTZ,
    last_error       TEXT,
    CONSTRAINT outbox_message_kind_valid CHECK (kind IN ('row_change', 'domain_event', 'audit_event')),
    CONSTRAINT outbox_message_action_valid CHECK (action IN ('insert', 'update', 'delete')),
    CONSTRAINT outbox_message_actor_type_valid
        CHECK (actor_type IN ('user', 'service', 'system', 'anonymous', 'unknown')),
    CONSTRAINT outbox_message_actor_valid CHECK (
        (actor_type = 'user') = (actor_user_id IS NOT NULL)
        AND (actor_type = 'service') = (actor_service IS NOT NULL)),
    CONSTRAINT outbox_message_actor_user_id_not_empty
        CHECK (actor_user_id <> '00000000-0000-0000-0000-000000000000'),
    CONSTRAINT outbox_message_attempts_valid CHECK (attempts >= 0)
);
-- Every insert, update and delete is short-lived churn, like kernel.idempotency.
ALTER TABLE recruitment.outbox_message SET (autovacuum_vacuum_scale_factor = 0.02, autovacuum_analyze_scale_factor = 0.02);
