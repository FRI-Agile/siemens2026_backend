# Recruitment

**Service / owner:** `RecruitmentModule` (`Api/Modules/RecruitmentModule`)
**Purpose:** the core of HealthyHires — job posts, the candidates who apply to them, their applications and the files
submitted with them, and the HR workflow. Staff
accounts are not stored here: they live in Microsoft Entra ID ([iam.md §2.4](../../architecture/iam.md#24-session-management)),
and the module refers to them by their object id (`oid`).

**Tables**

| Table | Purpose | Status |
| ----- | ------- | ------ |
| [`job_post`](#job_post) | A position the company is hiring for: a draft, published or closed | **Documented** |
| [`candidate`](#candidate) | A person who applied to at least one job post, one per e-mail | **Documented** |
| [`application`](#application) | A candidate's application to one job post, and where it is in the hiring pipeline | **Documented** |
| [`application_document`](#application_document) | A file submitted with an application (CV, cover letter, …): its type and its `files.file` | **Documented** |
| [`outbox_message`](#outbox_message) | The module's transactional outbox | **Documented** |

The DDL is in `configs/postgres/init/01-create-db.sql` ([ADR-D002](../decision-log.md#adr-d002--the-schema-is-an-init-ddl-script-until-production));
`RecruitmentDbContext` maps the tables with `ExcludeFromMigrations`. The schema is listed in `infra/db-schemas.txt`,
so `scripts/azure/db-grant-identity.sh` creates it in Azure.

**Functions**

| Name | Purpose |
| ---- | ------- |
| `recruitment.set_audit_fields()` | The [audit trigger](../conventions.md#audit-trigger-the-version--updated_at-rule) function: sets `updated_at`, bumps `version`. A copy in the module's own schema — see [ADR-D003](../decision-log.md#adr-d003--the-audit-trigger-function-lives-in-each-modules-schema) |
| `recruitment.immutable_unaccent(text)` | `public.unaccent` with its dictionary named by schema, declared `IMMUTABLE` so an index can store its result. Mapped in EF Core as `SearchFunctions.Unaccent` — see [ADR-D007](../decision-log.md#adr-d007--accent-insensitive-search-through-an-immutable-unaccent-wrapper) |

## `job_post`

> One job post, created by a hiring manager. Only a `published` one is seen by applicants; a `draft` (*koncept*) and
> a `closed` one are seen by staff only.

**Audit:** standard · **Soft delete:** no

**Columns**

| Column | Type | Key | Null | Default | Description |
| ------ | ---- | --- | ---- | ------- | ----------- |
| `id` | `UUID` | PK | NO | `uuidv7()` | App-assigned v7 GUID |
| `title` | `TEXT` |  | NO |  | The position's title |
| `location` | `TEXT` |  | NO |  | Where the work is done |
| `department` | `TEXT` |  | NO |  | The department the position belongs to |
| `description` | `TEXT` |  | NO |  | What the job is, as applicants read it |
| `status` | `TEXT` |  | NO | `'draft'` | Where the post is in its life — see CHECK |
| `open_positions` | `INTEGER` |  | NO |  | How many people are hired for the position |
| `salary` | `NUMERIC(10,2)` |  | NO |  | The wage in EUR, per hour, month or year as `payment_type` says |
| `payment_type` | `TEXT` |  | NO |  | How the wage is stated — see CHECK |
| `employment_type` | `TEXT` |  | NO |  | The form of employment (*typ pracovného pomeru*) — see CHECK |
| `recruiter_id` | `UUID` | LFK | NO |  | Entra object id of the staff member with the `Recruiter` role responsible for the hiring (*zodpovedný za realizáciu náboru*), chosen by the hiring manager who creates the post |

**Value sets**

| Column | Value | Meaning |
| ------ | ----- | ------- |
| `status` | `draft` | Being prepared (*koncept*), seen by staff only |
| `status` | `published` | Visible to applicants (*zverejnená*) |
| `status` | `closed` | No longer accepting applicants (*uzavretá*), hidden from them; can be reopened |
| `payment_type` | `hourly` | Hourly wage (*hodinová mzda*) |
| `payment_type` | `monthly` | Monthly wage (*mesačná mzda*) |
| `payment_type` | `yearly` | Yearly wage (*ročná mzda*) |
| `employment_type` | `full_time` | Permanent full-time employment (*trvalý pracovný pomer, TPP*) |
| `employment_type` | `part_time` | Part-time employment (*skrátený úväzok*) |
| `employment_type` | `fixed_term` | Fixed-term employment (*pracovný pomer na dobu určitú*) |
| `employment_type` | `work_performance_agreement` | *Dohoda o vykonaní práce* |
| `employment_type` | `work_activity_agreement` | *Dohoda o pracovnej činnosti* |
| `employment_type` | `student_work_agreement` | *Dohoda o brigádnickej práci študentov* (*brigádnická dohoda*) |
| `employment_type` | `self_employed` | Self-employed contractor (*živnosť*) |

The application's copies are `JobPostStatuses`, `PaymentTypes` and `EmploymentTypes` in `RecruitmentVocabulary.cs`;
change both together.

**Foreign keys**

| Column | References | On delete | Notes |
| ------ | ---------- | --------- | ----- |
| `recruiter_id` | Entra ID user (`oid`) | — | logical — no DB FK; the role is checked on write — see [ADR-D004](../decision-log.md#adr-d004--a-job-posts-recruiter-is-an-entra-object-id-checked-by-the-application) |

**Constraints**

| Name | Type | Definition | Purpose |
| ---- | ---- | ---------- | ------- |
| `job_post_title_valid` | CHECK | `btrim(title) <> ''` | No blank title |
| `job_post_location_valid` | CHECK | `btrim(location) <> ''` | No blank location |
| `job_post_department_valid` | CHECK | `btrim(department) <> ''` | No blank department |
| `job_post_description_valid` | CHECK | `btrim(description) <> ''` | No blank description |
| `job_post_status_valid` | CHECK | `status IN ('draft','published','closed')` | Guards the value set |
| `job_post_open_positions_valid` | CHECK | `open_positions > 0` | At least one position to fill |
| `job_post_salary_valid` | CHECK | `salary > 0` | A wage is always offered |
| `job_post_payment_type_valid` | CHECK | `payment_type IN ('hourly','monthly','yearly')` | Guards the value set |
| `job_post_employment_type_valid` | CHECK | `employment_type IN ('full_time','part_time','fixed_term','work_performance_agreement','work_activity_agreement','student_work_agreement','self_employed')` | Guards the value set |

**Indexes**

| Name | Columns | Type | Purpose |
| ---- | ------- | ---- | ------- |
| *(PK)* | `(id)` | btree | Lookup by id |
| `idx_job_post_recruiter_id` | `(recruiter_id)` | btree | A recruiter's own job posts |
| `idx_job_post_title` | `(title public.gin_trgm_ops)` | GIN trigram | The lists' search on the title |
| `idx_job_post_location` | `(location public.gin_trgm_ops)` | GIN trigram | The lists' search on the location |

**Triggers**

| Name | Timing | Action |
| ---- | ------ | ------ |
| `trg_job_post_set_audit` | BEFORE UPDATE | `recruitment.set_audit_fields()`: sets `updated_at`, bumps `version` |

**Notes**

- **Who writes what.** Only a hiring manager (or an administrator) creates a job post (`POST /job-posts`), and names
  its recruiter in the same request, choosing from `GET /recruiters`. Any hiring manager can then
  edit it (`PUT /job-posts/{id}`), close it (`POST …/close`) or reopen it (`POST …/reopen`), so the table keeps
  `updated_by`. Each of them carries the `version` the caller read ([optimistic locking](../conventions.md#optimistic-locking-version)).
- **Status moves `draft → published ⇄ closed`.** A post is created as a draft or published; editing can publish a
  draft. A published post never returns to a draft — applicants may have seen it — so it is closed instead. Only a
  published post can be closed, and only a closed one reopened, which publishes it again. A closed post is reopened
  before it is edited. The application enforces the transitions; the table only guards the value set.
- **Lengths are the application's.** The commands cap `title` at 200 characters, `location` and `department` at
  100, `description` at 3 000 and `open_positions` at 1000; the table only guards what must never be stored (blank
  text, no positions, no wage). `salary` fits `NUMERIC(10,2)`: at most 99 999 999.99, with 2 decimal places.
- **Public reads see published rows only.** `POST /job-posts/search` (a read with a body, offset pages, newest first
  by `created_at`, then `id`) and `GET /job-posts/{id}` are anonymous and filter on `status = 'published'`; a draft or
  closed post answers `404` like a missing post. They never return `recruiter_id` or the audit columns.
- **Staff see every row.** `POST /job-posts/manage/search` (hiring managers and administrators) lists every status,
  optionally filtered by `status`, `employmentType` and the same search, in the same order. It returns every column, `version`
  included, so the edit, close and reopen features can be driven from it.
- **Search uses the trigram indexes.** Both lists search `title ILIKE '%…%' OR location ILIKE '%…%'`
  (`JobPostQueryExtensions.MatchingSearch`). The [search-index convention](../conventions.md#search-indexes) gives
  each column its own trigram GIN index, which the planner combines with a `BitmapOr`; one index over both columns
  would only serve a query written against the same expression. A term under 3 characters has no trigram to look up
  and still scans. `JobPostQueryExtensionsTests` pins the query to both indexes.

## `candidate`

> A person who applied to at least one job post. One row per e-mail address, whatever its case.

**Audit:** standard, without `created_by` ([ADR-D008](../decision-log.md#adr-d008--rows-an-anonymous-applicant-creates-have-no-created_by)) · **Soft delete:** no

**Columns**

| Column | Type | Key | Null | Default | Description |
| ------ | ---- | --- | ---- | ------- | ----------- |
| `id` | `UUID` | PK | NO | `uuidv7()` | App-assigned v7 GUID |
| `first_name` | `TEXT` |  | NO |  | First name (*meno*) |
| `last_name` | `TEXT` |  | NO |  | Last name (*priezvisko*) |
| `display_name` | `TEXT` |  | NO | generated | `first_name \|\| ' ' \|\| last_name`, a stored generated column; the name search's column |
| `email` | `TEXT` | UQ | NO |  | Contact e-mail; unique on `lower(email)` |
| `phone_number` | `TEXT` |  | YES |  | Contact phone in E.164 (`+421905123456`); optional on the application form, which takes it in this form only |

**Constraints**

| Name | Type | Definition | Purpose |
| ---- | ---- | ---------- | ------- |
| `candidate_first_name_valid` | CHECK | `btrim(first_name) <> ''` | No blank first name |
| `candidate_last_name_valid` | CHECK | `btrim(last_name) <> ''` | No blank last name |
| `candidate_email_valid` | CHECK | `email ~ '^[^@\s]+@[^@\s]+$'` | One `@` between non-blank parts; the application validates further |
| `candidate_phone_number_valid` | CHECK | `phone_number ~ '^\+[1-9][0-9]{6,14}$'` | E.164 only, when given |

**Indexes**

| Name | Columns | Type | Purpose |
| ---- | ------- | ---- | ------- |
| *(PK)* | `(id)` | btree | Lookup by id |
| `uq_candidate_email` | `(lower(email))` | unique | One candidate per e-mail regardless of case; the lookup a new application makes |
| `idx_candidate_display_name` | `(recruitment.immutable_unaccent(display_name) public.gin_trgm_ops)` | GIN trigram | The search on the name, ignoring case and accents |
| `idx_candidate_email` | `(recruitment.immutable_unaccent(email) public.gin_trgm_ops)` | GIN trigram | The search on the e-mail, ignoring case and accents |

**Triggers**

| Name | Timing | Action |
| ---- | ------ | ------ |
| `trg_candidate_set_audit` | BEFORE UPDATE | `recruitment.set_audit_fields()`: sets `updated_at`, bumps `version` |

**Notes**

- **Identified by e-mail.** The first application carrying an e-mail creates the row; a later one with the same e-mail
  (any case) is linked to it. Applicants are anonymous and nothing verifies the address, so a later application does
  **not** change the stored name or phone — otherwise anyone could rewrite a candidate by applying with their e-mail.
  Staff correct the details, hence `updated_by`. The lookup is `lower(email) = lower(@email)`, both sides lowered by
  PostgreSQL as the unique index compares them; two first applications with one e-mail racing each other meet on the
  index, and the loser links to the winner's row.
- **One name column to search.** `display_name` is generated, so it cannot drift from the parts and the application
  never writes it (EF maps it `ValueGeneratedOnAddOrUpdate`). A search for "Martin Kaš" matches it, which two separate
  columns joined with `OR` would not.
- **Search is accent-insensitive.** `CandidateQueryExtensions.MatchingSearch` compares
  `immutable_unaccent(display_name)` and `immutable_unaccent(email)` with `ILIKE immutable_unaccent('%…%')`, so
  "kasuba" finds "Kašuba". Each column has its own trigram index over exactly that expression, combined with a
  `BitmapOr` ([search indexes](../conventions.md#search-indexes)); a query that drops the wrapper cannot use them.
  `CandidateQueryExtensionsTests` pins the query to both. The same indexes serve the trigram similarity operators
  (`%`, `<%`) should the search later rank fuzzy matches.

## `application`

> A candidate's application (*žiadosť*) to one published job post, and where it stands in the hiring pipeline.

**Audit:** standard, without `created_by` ([ADR-D008](../decision-log.md#adr-d008--rows-an-anonymous-applicant-creates-have-no-created_by)) · **Soft delete:** no

**Columns**

| Column | Type | Key | Null | Default | Description |
| ------ | ---- | --- | ---- | ------- | ----------- |
| `id` | `UUID` | PK | NO | `uuidv7()` | App-assigned v7 GUID |
| `job_post_id` | `UUID` | FK, UQ | NO |  | The post applied to |
| `candidate_id` | `UUID` | FK, UQ | NO |  | Who applied |
| `status` | `TEXT` |  | NO | `'nová žiadosť'` | Where the application is in the pipeline — see CHECK |

**Value sets**

| Column | Value | Meaning |
| ------ | ----- | ------- |
| `status` | `nová žiadosť` | Submitted, not yet looked at |
| `status` | `predvýber` | Shortlisted |
| `status` | `pohovor` | Invited to an interview |
| `status` | `ponuka` | Made an offer |
| `status` | `prijatý` | Accepted the offer and was hired |
| `status` | `odmietnutý` | Turned down |

The codes are the Slovak terms as written, diacritics included ([ADR-D009](../decision-log.md#adr-d009--new-value-sets-use-slovak-codes)). The application's
copy is `ApplicationStatuses` in `RecruitmentVocabulary.cs`; change both together.

**Foreign keys**

| Column | References | On delete | Notes |
| ------ | ---------- | --------- | ----- |
| `job_post_id` | `recruitment.job_post(id)` | `RESTRICT` | Posts are closed, never deleted; applications must not lose their post |
| `candidate_id` | `recruitment.candidate(id)` | `CASCADE` | An application means nothing without its candidate; erasing a candidate erases their applications |

**Constraints**

| Name | Type | Definition | Purpose |
| ---- | ---- | ---------- | ------- |
| `application_status_valid` | CHECK | `status IN ('nová žiadosť','predvýber','pohovor','ponuka','prijatý','odmietnutý')` | Guards the value set |

**Indexes**

| Name | Columns | Type | Purpose |
| ---- | ------- | ---- | ------- |
| *(PK)* | `(id)` | btree | Lookup by id |
| `uq_application_job_post_id_candidate_id` | `(job_post_id, candidate_id)` | unique | A candidate applies to a post once; leading with the post, also the job post detail's list of its applications |
| `idx_application_candidate_id` | `(candidate_id)` | btree | A candidate's applications; the cascade from `candidate` |

**Triggers**

| Name | Timing | Action |
| ---- | ------ | ------ |
| `trg_application_set_audit` | BEFORE UPDATE | `recruitment.set_audit_fields()`: sets `updated_at`, bumps `version` |

**Notes**

- **Only published posts take applications.** The submitting command (`SubmitApplication`) checks
  `job_post.status = 'published'`; a CHECK cannot look at another row. A draft answers `404`, as it is invisible to
  applicants, and a closed post `409`. Closing a post later leaves its applications as they are.
- **Applying twice to one post is refused, not merged.** The second application with the same e-mail (any case) is
  answered `409` and changes nothing: the applicant is anonymous, so replacing the first application's documents would
  let anyone who knows the e-mail overwrite it. A retry of the *same* submission carries the same `Idempotency-Key` and
  gets the first response replayed instead. Two submissions racing each other meet on
  `uq_application_job_post_id_candidate_id`, and the loser is answered the same `409`.
- **Status moves `nová žiadosť → predvýber → pohovor → ponuka → prijatý`, and `odmietnutý` from any step before
  `prijatý`.** Staff
  move it, carrying the `version` they read ([optimistic locking](../conventions.md#optimistic-locking-version)). The
  application enforces the moves; the table only guards the value set.
- **The job post detail lists its applications.** It reads `application` by `job_post_id` joined to `candidate` for
  the first name, last name and e-mail, and shows `status`; the position column is the post's own title.

## `application_document`

> A file the candidate submitted with an application — a CV, a cover letter or anything else: what it is, and which file
> it is. The file itself (name, type, size, bytes, malware scan) is the Files module's
> [`files.file`](files.md), referenced by `file_id`.

**Audit:** standard, without `created_by` and `updated_by` ([ADR-D008](../decision-log.md#adr-d008--rows-an-anonymous-applicant-creates-have-no-created_by)) · **Soft delete:** no · **Change-captured:** yes

**Columns**

| Column | Type | Key | Null | Default | Description |
| ------ | ---- | --- | ---- | ------- | ----------- |
| `id` | `UUID` | PK | NO | `uuidv7()` | App-assigned v7 GUID |
| `application_id` | `UUID` | FK | NO |  | The application the file belongs to |
| `type` | `TEXT` |  | NO |  | What the file is — see CHECK |
| `file_id` | `UUID` | UQ | NO |  | The file; logical reference → `files.file(id)`, no FK |

**Value sets**

| Column | Value | Meaning |
| ------ | ----- | ------- |
| `type` | `životopis` | Curriculum vitae |
| `type` | `motivačný list` | Cover letter |
| `type` | `iné` | Anything else: certificates, references, a portfolio |

The codes are the Slovak terms as written, diacritics included ([ADR-D009](../decision-log.md#adr-d009--new-value-sets-use-slovak-codes)). The application's
copy is `ApplicationDocumentTypes` in `RecruitmentVocabulary.cs`; change both together.

**Foreign keys**

| Column | References | On delete | Notes |
| ------ | ---------- | --------- | ----- |
| `application_id` | `recruitment.application(id)` | `CASCADE` | A file means nothing without its application; the cascade is invisible to change capture, so the deleting feature publishes `files_released` |
| `file_id` | `files.file(id)` | — | **logical — no DB FK**; the Files module follows it through the outbox |

**Constraints**

| Name | Type | Definition | Purpose |
| ---- | ---- | ---------- | ------- |
| `application_document_type_valid` | CHECK | `type IN ('životopis','motivačný list','iné')` | Guards the value set |

**Indexes**

| Name | Columns | Type | Purpose |
| ---- | ------- | ---- | ------- |
| *(PK)* | `(id)` | btree | Lookup by id |
| `idx_application_document_application_id` | `(application_id)` | btree | An application's documents; the cascade from `application` |
| `uq_application_document_file_id` | `(file_id)` | unique | A file belongs to one reference: two rows naming it would have the first one's release delete it under the second |
| `uq_application_document_application_id_type` | `(application_id, type) WHERE type IN ('životopis','motivačný list')` | unique, partial | At most one CV and one cover letter per application, so "the application's CV" is one row; the lookup the staff detail and the AI scoring make |

**Triggers**

| Name | Timing | Action |
| ---- | ------ | ------ |
| `trg_application_document_set_audit` | BEFORE UPDATE | `recruitment.set_audit_fields()`: sets `updated_at`, bumps `version` |

**Notes**

- **The file's lifecycle is the Files module's** ([files.md §9](../../api/files/files.md#9-confirm-and-release)). The
  entity is an `ITrackedEntity`, and the module registers `FileId` as a file column accepting `application_document`
  files. Inserting a row confirms its file once the outbox delivers the change; deleting it, or pointing it at another
  file, releases the old one. The write handler calls `IFileReferences.ValidateAsync` before saving, so a bad upload fails
  the write.
- **The CV is the row with `type = 'životopis'`.** The application form requires one and allows one cover letter
  (`motivačný list`); `uq_application_document_application_id_type` keeps both to one row per application, so the AI
  scoring (ADR-024) reads an application's CV by `(application_id, 'životopis')` with no ambiguity. The form takes up to
  3 `iné` documents (`ApplicationDocumentTypes.MaxOther`); the table does not limit them, so staff could add more later.
- **Every document is PDF, Word (`.doc`, `.docx`), JPEG or PNG, at most 10 MB** — the `application_document` file
  purpose, enforced by the Files module at upload and again on the stored object before the row is saved. A CV may
  therefore be an image, which the AI scoring has to read as one.
- **Each application references its own upload.** Re-sending a file id already attached to another application is
  refused with `400` on that field (`uq_application_document_file_id`): the client uploads the file again.
- **Staff see clean files only:** a file is downloadable once ClamAV cleared it, which the download endpoint checks.
- **Deletes the outbox cannot see.** Deleting an `application` or a `candidate` cascades to these rows in the database.
  That feature reads the rows' `file_id`s first and publishes `files_released` in the same transaction.

## `outbox_message`

The module's transactional outbox, as defined once in [conventions.md § Outbox pattern](../conventions.md#outbox-pattern-outbox_message).
Today it carries the changes of `application_document`, the only change-captured entity, and any `files_released`
event the module publishes.
