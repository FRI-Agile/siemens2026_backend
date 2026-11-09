# Files

**Service / owner:** Files module
**Purpose:** The metadata of every uploaded file: its kind, its name and type, where its bytes are in Blob Storage, and
where it is in its lifecycle. The bytes are in the `files` container. How the module uses the table:
[files.md](../../api/files/files.md).

Other modules reference a file by `id` as a logical reference, with no foreign key. The Files module confirms and
releases the file as the reference comes and goes, from the owning module's outbox (`FileReferenceHandler`).

**Tables**

| Table | Purpose | Status |
| ----- | ------- | ------ |
| [`file`](#file) | One uploaded object and its metadata | **Documented** |

## `file`

> One uploaded object's metadata. Soft-deletable, so a reference elsewhere does not dangle the moment its file is
> released.

**Audit:** custom (`uploaded_at`, `uploaded_by`; no `version`, no audit trigger) · **Soft delete:** yes

**Columns**

| Column | Type | Key | Null | Default | Description |
| ------ | ---- | --- | ---- | ------- | ----------- |
| `id` | `UUID` | PK | NO | `uuidv7()` | Assigned by the application (v7) when the upload URL is issued |
| `purpose` | `TEXT` |  | NO |  | The file's kind, an `IFilePurpose.Key` (`application_document`); selects its content rules |
| `object_key` | `TEXT` | UQ | NO |  | Blob name in the `files` container, `uploads/{id}.ext`; never the client's file name |
| `file_name` | `TEXT` |  | NO |  | As uploaded; shown to staff and used as the download's file name |
| `content_type` | `TEXT` |  | NO |  | Declared at intent; the stored blob must carry exactly this type to be confirmed |
| `size_bytes` | `BIGINT` |  | NO |  | Declared at intent, replaced by the blob's real size on confirm |
| `etag` | `TEXT` |  | YES |  | The blob's ETag, set on confirm; `NULL` while pending |
| `scanned_at` | `TIMESTAMPTZ` |  | YES |  | When ClamAV found the confirmed blob clean; only a clean file is downloadable |
| `uploaded_at` | `TIMESTAMPTZ` |  | NO | `now()` | When the upload URL was issued |
| `uploaded_by` | `UUID` |  | YES |  | Staff Entra `oid`; `NULL` for an anonymous applicant (logical, no FK, ADR-D004) |
| `deleted_at` | `TIMESTAMPTZ` |  | YES |  | Released (soft delete) |
| `deleted_by` | `UUID` |  | YES |  | Staff Entra `oid` whose change released it; `NULL` for the system or an applicant |

**Checks**

| Name | Rule |
| ---- | ---- |
| `file_purpose_valid`, `file_object_key_valid`, `file_file_name_valid`, `file_content_type_valid` | not blank |
| `file_size_bytes_valid` | `size_bytes > 0` |
| `file_scanned_at_valid` | `scanned_at IS NULL OR etag IS NOT NULL`: only a confirmed file is scanned |

**Indexes**

| Name | Columns | Type | Purpose |
| ---- | ------- | ---- | ------- |
| `uq_file_object_key` | `(object_key)` | unique | One row per blob |
| `idx_file_pending_uploaded_at` | `(uploaded_at)` **WHERE** `etag IS NULL` | btree | `FileJanitor` reap: files pending for over a day, oldest first |
| `idx_file_unscanned_uploaded_at` | `(uploaded_at)` **WHERE** `etag IS NOT NULL AND scanned_at IS NULL AND deleted_at IS NULL` | btree | `FileScanner` backlog, oldest first |
| `idx_file_deleted_at` | `(deleted_at)` **WHERE** `deleted_at IS NOT NULL` | btree | `FileJanitor` retention sweep of released files |

**Notes**

- **No status column.** The state follows from `etag`, `scanned_at` and `deleted_at`: pending, confirmed, clean,
  released ([files.md §2](../../api/files/files.md#2-lifecycle-of-a-file)).
- **No `version`.** After the insert only the system writes a row (the outbox's confirm and release, the scan, the
  janitor), and each write sets
  columns no other write sets. `uploaded_at` / `uploaded_by` stand for `created_at` / `created_by`, as on other tables an
  anonymous applicant writes (ADR-D008).
- **All indexes but the key are partial.** The workers only ever look at the short-lived pending, unscanned and
  released minorities, so each `ORDER BY … LIMIT` batch drains oldest first from a small index.
- **Not audited.** Uploads and the workers' maintenance are not user changes to business records; the referencing
  module's own record is what staff act on.
