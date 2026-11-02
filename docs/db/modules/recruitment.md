# Recruitment

**Service / owner:** `RecruitmentModule` (`Api/Modules/RecruitmentModule`)
**Purpose:** the core of HealthyHires — job posts, and later applications, CVs and the HR workflow. Staff
accounts are not stored here: they live in Microsoft Entra ID ([iam.md §2.4](../../architecture/iam.md#24-session-management)),
and the module refers to them by their object id (`oid`).

**Tables**

| Table | Purpose | Status |
| ----- | ------- | ------ |
| [`job_post`](#job_post) | A position the company is hiring for: a draft, published or closed | **Documented** |

The DDL is in `configs/postgres/init/01-create-db.sql` ([ADR-D002](../decision-log.md#adr-d002--the-schema-is-an-init-ddl-script-until-production));
`RecruitmentDbContext` maps the tables with `ExcludeFromMigrations`. The schema is listed in `infra/db-schemas.txt`,
so `scripts/azure/db-grant-identity.sh` creates it in Azure.

**Functions**

| Name | Purpose |
| ---- | ------- |
| `recruitment.set_audit_fields()` | The [audit trigger](../conventions.md#audit-trigger-the-version--updated_at-rule) function: sets `updated_at`, bumps `version`. A copy in the module's own schema — see [ADR-D003](../decision-log.md#adr-d003--the-audit-trigger-function-lives-in-each-modules-schema) |

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
- **Public reads see published rows only.** `GET /job-posts` (offset pages, newest first by `created_at`, then `id`)
  and `GET /job-posts/{id}` are anonymous and filter on `status = 'published'`; a draft or closed post answers `404`
  like a missing post.
  They never return `recruiter_id` or the audit columns.
- **No search index yet — a deviation.** The public list searches `title` and `location` with `ILIKE '%…%'`. The
  [search-index convention](../conventions.md#search-indexes) calls for a trigram GIN index, but that needs the
  `pg_trgm` extension allow-listed on the Azure server, and one company's job posts are few enough to scan. Add it
  when the table grows or the plan shows the scan costing.
