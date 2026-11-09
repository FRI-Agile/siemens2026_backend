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
| [`application_document`](#application_document) | A file submitted with an application (CV, cover letter, …) and its upload state | **Documented** |

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
  its recruiter in the same request, choosing from `GET /staff-users?role=Recruiter`. Any hiring manager can then
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
| `phone_number` | `TEXT` |  | NO |  | Contact phone in E.164 (`+421905123456`), normalised by the application |

**Constraints**

| Name | Type | Definition | Purpose |
| ---- | ---- | ---------- | ------- |
| `candidate_first_name_valid` | CHECK | `btrim(first_name) <> ''` | No blank first name |
| `candidate_last_name_valid` | CHECK | `btrim(last_name) <> ''` | No blank last name |
| `candidate_email_valid` | CHECK | `email ~ '^[^@\s]+@[^@\s]+$'` | One `@` between non-blank parts; the application validates further |
| `candidate_phone_number_valid` | CHECK | `phone_number ~ '^\+[1-9][0-9]{6,14}$'` | E.164 only |

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
  Staff correct the details, hence `updated_by`.
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
| `status` | `TEXT` |  | NO | `'new'` | Where the application is in the pipeline — see CHECK |

**Value sets**

| Column | Value | Meaning |
| ------ | ----- | ------- |
| `status` | `new` | Submitted, not yet looked at (*nová žiadosť*) |
| `status` | `screening` | Shortlisted (*predvýber*) |
| `status` | `interview` | Invited to an interview (*pohovor*) |
| `status` | `offer` | Made an offer (*ponuka*) |
| `status` | `hired` | Accepted the offer and was hired (*prijatý*) |
| `status` | `rejected` | Turned down (*odmietnutý*) |

The application's copy is `ApplicationStatuses` in `RecruitmentVocabulary.cs`; change both together.

**Foreign keys**

| Column | References | On delete | Notes |
| ------ | ---------- | --------- | ----- |
| `job_post_id` | `recruitment.job_post(id)` | `RESTRICT` | Posts are closed, never deleted; applications must not lose their post |
| `candidate_id` | `recruitment.candidate(id)` | `CASCADE` | An application means nothing without its candidate; erasing a candidate erases their applications |

**Constraints**

| Name | Type | Definition | Purpose |
| ---- | ---- | ---------- | ------- |
| `application_status_valid` | CHECK | `status IN ('new','screening','interview','offer','hired','rejected')` | Guards the value set |

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

- **Only published posts take applications.** The submitting command checks `job_post.status = 'published'`; a CHECK
  cannot look at another row. Closing a post later leaves its applications as they are.
- **Status moves `new → screening → interview → offer → hired`, and `rejected` from any step before `hired`.** Staff
  move it, carrying the `version` they read ([optimistic locking](../conventions.md#optimistic-locking-version)). The
  application enforces the moves; the table only guards the value set.
- **The job post detail lists its applications.** It reads `application` by `job_post_id` joined to `candidate` for
  the first name, last name and e-mail, and shows `status`; the position column is the post's own title.

## `application_document`

> A file the candidate submitted with an application — a CV, a cover letter or anything else — and where its upload
> stands. The blob itself is in the module's Blob Storage container, named by `id`.

**Audit:** standard, without `created_by` and `updated_by` ([ADR-D008](../decision-log.md#adr-d008--rows-an-anonymous-applicant-creates-have-no-created_by)) · **Soft delete:** no

**Columns**

| Column | Type | Key | Null | Default | Description |
| ------ | ---- | --- | ---- | ------- | ----------- |
| `id` | `UUID` | PK | NO | `uuidv7()` | App-assigned v7 GUID; also the blob's name ([ADR-A010](../../architecture/ADR-ASPNET.md#adr-a010-file-storage--azure-blob-storage-via-azure-sdk)) |
| `application_id` | `UUID` | FK | NO |  | The application the file belongs to |
| `type` | `TEXT` |  | NO |  | What the file is — see CHECK |
| `file_name` | `TEXT` |  | NO |  | The name it was uploaded under; shown to staff and given to downloads, never used as the blob's name |
| `content_type` | `TEXT` |  | NO |  | The MIME type, checked against the blob on confirmation |
| `size_bytes` | `BIGINT` |  | NO |  | The size, checked against the blob on confirmation |
| `status` | `TEXT` |  | NO | `'uploading'` | Where the upload stands — see CHECK |

**Value sets**

| Column | Value | Meaning |
| ------ | ----- | ------- |
| `type` | `cv` | Curriculum vitae (*životopis*) |
| `type` | `cover_letter` | Cover letter (*motivačný list*) |
| `type` | `other` | Anything else: certificates, references, a portfolio |
| `status` | `uploading` | The upload URL is issued; not confirmed yet |
| `status` | `scanning` | Confirmed, size and type match; waiting for ClamAV. Hidden from staff |
| `status` | `clean` | ClamAV found it clean; staff may download it |

The application's copies are `ApplicationDocumentTypes` and `ApplicationDocumentStatuses` in
`RecruitmentVocabulary.cs`; change both together.

**Foreign keys**

| Column | References | On delete | Notes |
| ------ | ---------- | --------- | ----- |
| `application_id` | `recruitment.application(id)` | `CASCADE` | A file means nothing without its application; the blob is deleted by the application, as the database cannot reach it |

**Constraints**

| Name | Type | Definition | Purpose |
| ---- | ---- | ---------- | ------- |
| `application_document_type_valid` | CHECK | `type IN ('cv','cover_letter','other')` | Guards the value set |
| `application_document_file_name_valid` | CHECK | `btrim(file_name) <> ''` | No blank file name |
| `application_document_content_type_valid` | CHECK | `btrim(content_type) <> ''` | No blank content type |
| `application_document_size_bytes_valid` | CHECK | `size_bytes > 0` | No empty files |
| `application_document_status_valid` | CHECK | `status IN ('uploading','scanning','clean')` | Guards the value set |

**Indexes**

| Name | Columns | Type | Purpose |
| ---- | ------- | ---- | ------- |
| *(PK)* | `(id)` | btree | Lookup by id, e.g. for a download URL |
| `idx_application_document_application_id` | `(application_id)` | btree | An application's documents; the cascade from `application` |
| `idx_application_document_created_at` | `(created_at) WHERE status = 'uploading'` | partial | The pruner's unconfirmed uploads, oldest first; holds only that short-lived backlog |

**Triggers**

| Name | Timing | Action |
| ---- | ------ | ------ |
| `trg_application_document_set_audit` | BEFORE UPDATE | `recruitment.set_audit_fields()`: sets `updated_at`, bumps `version` |

**Notes**

- **The upload lifecycle is ADR-A010's.** Issuing an upload URL inserts the row as `uploading`; the candidate's
  confirmation checks the blob's real size and type and moves it to `scanning`; ClamAV's verdict moves it to `clean`.
  An infected file, and an upload left `uploading` for an hour, are deleted with their row. `version` keeps the
  pruner and a late confirmation from both winning.
- **Staff see `clean` files only**, so every staff read filters on it. Lengths and the allowed content types and sizes
  are the application's rules, checked before the upload URL is issued.
