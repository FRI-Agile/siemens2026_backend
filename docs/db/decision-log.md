# Decision log (ADR-lite)

> Append-only. Each entry: date, decision, and the reasoning. Never edit past entries —
> supersede them with a new one.

---

## ADR-D001 — Idempotency records get a shared `kernel` schema, not a module's

**Date:** 2026-10-13 · **Status:** accepted

`kernel.idempotency` holds the HTTP `Idempotency-Key` records ([ADR-A016](../architecture/ADR-ASPNET.md#adr-a016-idempotency--idempotency-key-middleware)).
A claim is taken by host middleware *before* routing has selected a handler, so there is no module to attribute
it to, and putting it in one would make that module look like its owner.

The record therefore cannot be atomic with the write it protects, and does not try to be. It closes the restart
and multi-instance windows (a claim outlives the process that took it), leaving a smaller one: a crash between
the endpoint's commit and the response being captured. Closing that needs a natural key on the business row,
decided per endpoint.

Two consequences the table's shape encodes:

- **`lease_expires_at`.** A process that dies mid-request never completes or abandons its claim. Without a
  takeover window every retry of that key would get `409` until the full TTL elapses. The lease must exceed the
  slowest covered endpoint, or a duplicate gets in while the work is still running.
- **Autovacuum.** Rows are inserted, updated once and deleted within the TTL, so dead tuples pile up far faster
  than the row count suggests. The table carries `autovacuum_vacuum_scale_factor = 0.02` rather than the 20%
  default, set in the DDL.

---

## ADR-D002 — The schema is an init DDL script until production

**Date:** 2026-10-13 · **Status:** accepted

`configs/postgres/init/01-create-db.sql` is the schema. The AppHost loads it into a fresh local database on every
run, and it is edited in place; the EF Core contexts map their tables with `ExcludeFromMigrations`. This narrows
[ADR-A005](../architecture/ADR-ASPNET.md#adr-a005-persistence--postgresql-with-entity-framework-core) for the
pre-production phase.

- There is no data to migrate yet, so `ALTER` steps would only record churn; one script reads as the whole model.
- The DDL can say what EF Core's model cannot (storage parameters, triggers, partial indexes) without hand-edited migrations.
- Before the first production deployment, this script becomes the baseline and later changes need migrations — a
  decision still to be made.

---

## ADR-D003 — The audit trigger function lives in each module's schema

**Date:** 2026-10-21 · **Status:** accepted

The [audit trigger](conventions.md#audit-trigger-the-version--updated_at-rule) function is created as
`<module>.set_audit_fields()`, one copy per module schema, starting with `recruitment.set_audit_fields()`.

- A module's schema stays self-contained: extracting a module ([ADR-A001](../architecture/ADR-ASPNET.md#adr-a001-application-architecture--modular-monolith))
  takes its tables and their trigger function along, with nothing left pointing at `public` or `kernel`.
- The function is five lines and never changes, so the duplication costs nothing in practice.

---

## ADR-D004 — A job post's recruiter is an Entra object id, checked by the application

**Date:** 2026-10-21 · **Status:** accepted

`recruitment.job_post.recruiter_id` holds the Entra object id (`oid`) of the responsible recruiter. Staff accounts
and their app roles live only in Entra ([iam.md §2.4](../architecture/iam.md#24-session-management)); there is no
staff table to reference.

- There is no foreign key and no role constraint. The handler creating the job post asks the directory (Microsoft Graph)
  that the user holds the `Recruiter` role at that moment.
- A recruiter who later loses the role or is deleted in Entra stays on the job post. Noticing that would need Entra
  change notifications, which nothing needs yet.
- `created_by` and `updated_by` hold Entra object ids for the same reason, as every staff reference will.

---

## ADR-D005 — The DDL script is applied to Azure once, as the baseline

**Date:** 2026-10-21 · **Status:** accepted · **Follows up:** ADR-D002

`scripts/azure/db-schema.sh` (`just azure-db-schema`) creates the tables in a new Azure database from
`configs/postgres/init/01-create-db.sql`, the script the AppHost loads locally. Until now nothing created them in
Azure: `db-grant-identity.sh` made the roles and schemas, the EF contexts map their tables with `ExcludeFromMigrations`,
and the deploy workflow touches no schema.

- It runs as the operator acting as `healthyhires_app`, in one transaction, so every object belongs to the application
  role, like the schemas. The `CREATE SCHEMA` lines are skipped: the role may not create schemas, even with
  `IF NOT EXISTS`, and the script checks that `infra/db-schemas.txt` lists every schema the DDL creates.
- `kernel` joined `infra/db-schemas.txt`; without it `kernel.idempotency` had no schema in Azure.
- Only `01-create-db.sql`: the development seed never reaches Azure.
- It refuses a database that already has tables, because the DDL is not re-runnable. From the first deployment on, the
  script is the baseline ADR-D002 anticipated, and a schema change needs a migration. How migrations are written and
  applied is still open; numbered SQL files applied through the same tunnel fit the SQL-first approach best. Until
  then a change is applied by hand, and the DDL script is updated in the same pull request.

---

## ADR-D006 — Extensions live in `public` and are created by the operator

**Date:** 2026-11-02 · **Status:** accepted · **Follows up:** ADR-D005

The first extension, `pg_trgm` (the job post search's trigram indexes), is created `WITH SCHEMA public` by the DDL
script locally and by `db-grant-identity.sh` (`just azure-db-grant`) in Azure, after `infra/modules/postgres.bicep`
allow-lists it in the server's `azure.extensions` parameter.

- Azure only lets an allow-listed extension be created, and creating one needs the `CREATE` privilege on the database,
  which `healthyhires_app` does not have (ADR-D005). So the operator creates it, as with the schemas; `db-schema.sh`
  skips the `CREATE EXTENSION` lines and refuses to run until they exist, and `migrate.sh import` leaves the
  extensions out of the restore.
- An extension belongs to the database, not to a module, so it does not follow ADR-D003: a copy per module schema is
  impossible (an extension is installed once), and `public` is where every schema can reach it. Its objects are
  written schema-qualified, e.g. `public.gin_trgm_ops`.
- `db-grant-identity.sh` reads the extensions from the DDL, so a new one is added there and to `azure.extensions`;
  `azure.extensions` is a list, added to rather than replaced.

---

## ADR-D007 — Accent-insensitive search through an immutable `unaccent` wrapper

**Date:** 2026-11-09 · **Status:** accepted · **Follows up:** ADR-D006

Staff search candidates by name and e-mail, and Slovak names carry accents a searcher often leaves out ("Kasuba" for
"Kašuba"). The `unaccent` extension strips them, created `WITH SCHEMA public` by the operator like `pg_trgm`
(ADR-D006) and allow-listed in `azure.extensions`.

- `unaccent()` is only `STABLE` — it finds its dictionary through `search_path` — so an index cannot store its result.
  `recruitment.immutable_unaccent(text)` calls `public.unaccent('public.unaccent'::regdictionary, $1)`, naming both by
  schema, and is declared `IMMUTABLE`; the trigram indexes are built over it, and a search wraps both the column and
  the term in it. EF Core maps it as `SearchFunctions.Unaccent`.
- The wrapper is a copy in the module's schema, like the audit trigger (ADR-D003), not in `public`: the application
  role owns `recruitment` and may create it there, which it may not in `public`, and the module stays
  self-contained when extracted.
- Editing the `unaccent` rules file would change what the function returns for stored rows, so it means `REINDEX`ing
  the indexes over it. Nobody edits it today.

---

## ADR-D008 — Rows an anonymous applicant creates have no `created_by`

**Date:** 2026-11-09 · **Status:** accepted

Applicants have no account ([ADD §2](../architecture/ADD.md#2-purpose)), so the rows an application creates —
`candidate`, `application`, `application_document` — have no user to put in `created_by`.

- These tables leave the column out rather than make it nullable: it would be `NULL` on every row, and a sentinel id
  would pretend to be a user. Who created the row is implied by the table.
- `candidate` and `application` keep `updated_by`, because staff edit them. `application_document` drops it too: only
  the system moves its status.
- `version` and the audit trigger stay on all three. A candidate is identified by e-mail (`uq_candidate_email`), and a
  later application with a known e-mail is linked to the existing candidate without changing it, since nothing proves
  the applicant owns the address. *(Superseded by ADR-D010: `candidate` is gone, and its details live on
  `application`, which keeps the same rules.)*

---

## ADR-D009 — New value sets use Slovak codes

**Date:** 2026-11-09 · **Status:** accepted

The application is Slovak-only, and staff read the codes directly — in the database, in logs, in API responses. An
English code (`screening`) is one more name to translate back to the term the business uses (*predvýber*). From
schema 0.4.0 on, a `CHECK`-guarded value set stores the Slovak term itself, as written: `nová žiadosť`,
`motivačný list`.

- Diacritics and spaces are kept. The database is UTF-8 and `TEXT` stores them like any other character, so the code
  is the word the UI shows and needs no lookup; the UI only capitalises its first letter (*Nová žiadosť*).
- Codes are lowercase and in Unicode NFC, the form C# string literals and browsers produce. A comparison is by exact
  code points, so a client that sent the decomposed form (NFD) would fail the API's validation and the `CHECK`;
  clients send the codes the API returns, never retyped ones. In a URL a code is percent-encoded like any other text.
- C# identifiers stay English (`ApplicationStatuses.Screening = "predvýber"`), like the rest of the code; only the
  stored value is Slovak.
- `job_post`'s value sets (`status`, `payment_type`, `employment_type`) predate this decision and keep their English
  codes. Moving them means a data migration in Azure and a matching API and frontend change, so it is a change of its
  own.

---

## ADR-D010 — Applicants belong to the job post; no shared candidate

**Date:** 2026-11-10 · **Status:** accepted · **Supersedes:** the `candidate` table of schema 0.4.0 (ADR-D008's note)

Staff work with applicants one job post at a time: the job post detail lists who applied to that post and moves each
through the pipeline. No screen shows a person across posts, or which posts someone applied to. A shared `candidate`
row, one per e-mail, linked applications nobody looks at together — and cost a lookup, a race between two first
applications with one e-mail, and a rule that a later application must not change the stored name or phone.

- `recruitment.candidate` is dropped. `application` carries `first_name`, `last_name`, the generated `display_name`,
  `email` and `phone_number`, as typed on that post's form, with the same CHECKs and the same accent-insensitive
  trigram indexes.
- Someone applying to two posts is two unrelated rows, each with its own details, documents and status. Correcting
  one does not touch the other.
- A post takes one application per e-mail, compared without case: `uq_application_job_post_id_email` on
  `(job_post_id, lower(email))`. A second one is refused with `409`, not merged, since nothing proves the applicant owns
  the address.
- Erasing a person's data (GDPR) means deleting their applications by e-mail, each publishing `files_released`.
- Should a cross-post view ever be needed, it can group applications by `lower(email)`, which the unique index's
  expression already supports per post.

---

## ADR-D011 — Staff can add an applicant by hand, recorded with `is_manual` and `created_by`

**Date:** 2026-11-12 · **Status:** accepted · **Amends:** ADR-D008

A recruiter sometimes gets a candidate outside the public form: details sent by e-mail, or a referral from an
employee. They add the candidate to a published post assigned to them (an administrator to any published post), and
it then goes through the pipeline like a submitted application.

- The row stays in `recruitment.application`: a manual candidate is an application to one post like any other
  (ADR-D010), and the same `uq_application_job_post_id_email` refuses a second one with the same e-mail on that post,
  whichever way either came in.
- `is_manual BOOLEAN NOT NULL DEFAULT false` marks it, so staff can tell it was not submitted by the candidate.
- A manual row has a user to name, so `application` gains a nullable `created_by`: set on every manual row, `NULL` on
  every submitted one, and `application_manual_has_creator` (`is_manual = (created_by IS NOT NULL)`) keeps the two
  together. ADR-D008 still holds for the submitted rows, which have no user; the column being `NULL` there now says
  "submitted", not a missing value.
- It starts in `nová žiadosť`, like a submitted application, and carries no documents when created: they are attached
  afterwards, as a separate step.

