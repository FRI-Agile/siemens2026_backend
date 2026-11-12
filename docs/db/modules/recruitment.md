# Recruitment

**Service / owner:** `RecruitmentModule` (`Api/Modules/RecruitmentModule`)
**Purpose:** the core of HealthyHires — job posts, the applications to them (each carrying who applied) and the files
submitted with them, and the HR workflow. Staff
accounts are not stored here: they live in Microsoft Entra ID ([iam.md §2.4](../../architecture/iam.md#24-session-management)),
and the module refers to them by their object id (`oid`).

**Tables**

| Table | Purpose | Status |
| ----- | ------- | ------ |
| [`job_post`](#job_post) | A position the company is hiring for: a draft, published or closed | **Documented** |
| [`application`](#application) | One person's application to one job post: who applied, and where it is in the hiring pipeline | **Documented** |
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
| `idx_job_post_title` | `(recruitment.immutable_unaccent(title) public.gin_trgm_ops)` | GIN trigram | The lists' fuzzy search on the title: unaccented `ILIKE` and word similarity (`<%`) |
| `idx_job_post_location` | `(recruitment.immutable_unaccent(location) public.gin_trgm_ops)` | GIN trigram | The lists' fuzzy search on the location: unaccented `ILIKE` and word similarity (`<%`) |

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
- **Public reads see published rows only.** `POST /job-posts/search` (a read with a body, offset pages, sorted as
  below, optionally filtered by `employmentTypes`) and `GET /job-posts/{id}` are anonymous and filter on
  `status = 'published'`; a draft or closed post answers `404` like a missing post. They never return `recruiter_id` or
  the audit columns.
- **Staff see every row.** `POST /job-posts/manage/search` (hiring managers and administrators) lists every status,
  optionally filtered by `statuses`, and takes the same `employmentTypes`, search and sorts. It returns every column,
  `version` included, so the edit, close and reopen features can be driven from it.
- **The filters are lists.** `statuses` and `employmentTypes` each match a post with any of their values; empty or
  left out, they match every post. An unknown value is a `400`.
- **Both lists sort by `sort`: `newest` (the default), `oldest` or `relevance`.** `newest` and `oldest` order by
  `created_at`, then `id`. `relevance` needs a search text (`400` without one; the app switches to it while a search is
  typed) and orders by `GREATEST(strict_word_similarity(term, title), strict_word_similarity(term, location))`
  descending, newest first among equals (`JobPostQueryExtensions.SortedBy`). Strict word similarity keeps to word
  boundaries, so a typo of "Skladník" ranks that title above "Vedúci skladníkov"; the ranking is computed per matched
  row, not served by an index.
- **Search is accent-insensitive, fuzzy under `relevance`, and uses the trigram indexes.** Both lists match the title
  or the location by `immutable_unaccent(col) ILIKE immutable_unaccent('%…%')`, so "kosice" finds "Košice"; sorted by
  `relevance` they also match by `immutable_unaccent(term) <% immutable_unaccent(col)` (pg_trgm word similarity,
  default threshold 0.6), so a typo like "elektrikár udrzbz" still finds "Elektrikár údržby"
  (`JobPostQueryExtensions.MatchingSearch`). The
  [search-index convention](../conventions.md#search-indexes) gives each column its own trigram GIN index over the
  unaccented text, which the planner combines with a `BitmapOr`; a query that drops the `immutable_unaccent` wrapper
  cannot use them. A term under 3 characters has no trigram to look up and still scans.
  `JobPostQueryExtensionsTests` pins the query to both indexes.

## `application`

> One person's application (*žiadosť*) to one published job post: who applied, as they filled in the form, and where it
> stands in the hiring pipeline. Each job post has its own applicants; there is no shared candidate table
> ([ADR-D010](../decision-log.md#adr-d010--applicants-belong-to-the-job-post-no-shared-candidate)).

**Audit:** standard, without `created_by` ([ADR-D008](../decision-log.md#adr-d008--rows-an-anonymous-applicant-creates-have-no-created_by)) · **Soft delete:** no

**Columns**

| Column | Type | Key | Null | Default | Description |
| ------ | ---- | --- | ---- | ------- | ----------- |
| `id` | `UUID` | PK | NO | `uuidv7()` | App-assigned v7 GUID |
| `job_post_id` | `UUID` | FK, UQ | NO |  | The post applied to |
| `first_name` | `TEXT` |  | NO |  | First name (*meno*) |
| `last_name` | `TEXT` |  | NO |  | Last name (*priezvisko*) |
| `display_name` | `TEXT` |  | NO | generated | `first_name \|\| ' ' \|\| last_name`, a stored generated column; the name search's column |
| `email` | `TEXT` | UQ | NO |  | Contact e-mail; unique per post on `lower(email)` |
| `phone_number` | `TEXT` |  | YES |  | Contact phone in E.164 (`+421905123456`); optional on the application form, which takes it in this form only |
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

**Constraints**

| Name | Type | Definition | Purpose |
| ---- | ---- | ---------- | ------- |
| `application_first_name_valid` | CHECK | `btrim(first_name) <> ''` | No blank first name |
| `application_last_name_valid` | CHECK | `btrim(last_name) <> ''` | No blank last name |
| `application_email_valid` | CHECK | `email ~ '^[^@\s]+@[^@\s]+$'` | One `@` between non-blank parts; the application validates further |
| `application_phone_number_valid` | CHECK | `phone_number ~ '^\+[1-9][0-9]{6,14}$'` | E.164 only, when given |
| `application_status_valid` | CHECK | `status IN ('nová žiadosť','predvýber','pohovor','ponuka','prijatý','odmietnutý')` | Guards the value set |

**Indexes**

| Name | Columns | Type | Purpose |
| ---- | ------- | ---- | ------- |
| *(PK)* | `(id)` | btree | Lookup by id |
| `uq_application_job_post_id_email` | `(job_post_id, lower(email))` | unique | One application per e-mail and post, regardless of case |
| `idx_application_job_post_id_created_at` | `(job_post_id, created_at, id)` | btree | A post's applicants newest or oldest first, a page at a time, without a sort; read backwards for newest |
| `idx_application_display_name` | `(recruitment.immutable_unaccent(display_name) public.gin_trgm_ops)` | GIN trigram | The fuzzy name search: unaccented `ILIKE` and word similarity (`<%`) |
| `idx_application_email` | `(email public.gin_trgm_ops)` | GIN trigram | The e-mail search, a plain `ILIKE '%…%'` |

**Triggers**

| Name | Timing | Action |
| ---- | ------ | ------ |
| `trg_application_set_audit` | BEFORE UPDATE | `recruitment.set_audit_fields()`: sets `updated_at`, bumps `version` |

**Notes**

- **The applicant belongs to the post.** The row carries the name, e-mail and phone exactly as typed on that post's
  form. Someone applying to two posts is two unrelated rows, each with its own details and status; nothing links them,
  because no screen shows a person across posts ([ADR-D010](../decision-log.md#adr-d010--applicants-belong-to-the-job-post-no-shared-candidate)).
  Staff correct the details on the row, hence `updated_by`.
- **Only published posts take applications.** The submitting command (`SubmitApplication`) checks
  `job_post.status = 'published'`; a CHECK cannot look at another row. A draft answers `404`, as it is invisible to
  applicants, and a closed post `409`. Closing a post later leaves its applications as they are.
- **Applying twice to one post is refused, not merged.** A second application with the same e-mail (any case) to the
  same post is answered `409` and changes nothing: the applicant is anonymous, so replacing the first application's
  details or documents would let anyone who knows the e-mail overwrite it. A retry of the *same* submission carries the
  same `Idempotency-Key` and gets the first response replayed instead. Two submissions racing each other meet on
  `uq_application_job_post_id_email`, and the loser is answered the same `409`.
- **Status moves `nová žiadosť → predvýber → pohovor → ponuka → prijatý`, and `odmietnutý` from any step before
  `prijatý`.** Staff move it, carrying the `version` they read ([optimistic locking](../conventions.md#optimistic-locking-version)). The
  application enforces the moves; the table only guards the value set.
- **The job post detail lists its applications** (`ListApplications`) — first name, last name, e-mail and status — by
  `job_post_id`, with no join; the position column is the post's own title. Newest or oldest first, by
  `(created_at, id)` so offset pages neither repeat nor skip a row, served by `idx_application_job_post_id_created_at`.
  Who may list them is the post's: an administrator any post, a recruiter the posts assigned to them (`recruiter_id`),
  a hiring manager the posts they created (`created_by`), as `JobPostAccess` decides; any other post answers `404`.
- **One name column to search.** `display_name` is generated, so it cannot drift from the parts and the application
  never writes it (EF maps it `ValueGeneratedOnAddOrUpdate`). A search for "Martin Kaš" matches it, which two separate
  columns joined with `OR` would not.
- **The name search is accent-insensitive and fuzzy; the e-mail search is a plain contains.**
  `ApplicationQueryExtensions.MatchingSearch` matches `immutable_unaccent(display_name) ILIKE
  immutable_unaccent('%…%')` or `immutable_unaccent(term) <% immutable_unaccent(display_name)` (pg_trgm word
  similarity, default threshold 0.6), so "kasuba" and the typo "kasubo" find "Kašuba", while "kasbua" does not. The
  e-mail matches `email ILIKE '%…%'`. Each branch is served by its column's trigram index, combined with a `BitmapOr`
  ([search indexes](../conventions.md#search-indexes)); a name query that drops the `immutable_unaccent` wrapper cannot
  use its index. `ApplicationQueryExtensionsTests` pins the query to both.

## `application_document`

> A file the applicant submitted with an application — a CV, a cover letter or anything else: what it is, and which file
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
- **Deletes the outbox cannot see.** Deleting an `application` cascades to these rows in the database. That feature
  reads the rows' `file_id`s first and publishes `files_released` in the same transaction.

## `outbox_message`

The module's transactional outbox, as defined once in [conventions.md § Outbox pattern](../conventions.md#outbox-pattern-outbox_message).
Today it carries the changes of `application_document`, the only change-captured entity, and any `files_released`
event the module publishes.
