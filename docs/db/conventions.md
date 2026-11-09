# Conventions & standards

These rules are assumed for every table. A table entry only mentions them when it **deviates**.

## Naming

| Object | Rule | Example |
| ------ | ---- | ------- |
| Schema / module | `snake_case`, singular | `recruitment` |
| Table | `snake_case`, singular noun | `job_offer` |
| Junction (M:N) table | `<owner>_<target>` | `job_offer_skill` |
| Column | `snake_case` | `country_code` |
| Primary key column | `id` for surrogate keys, natural code for reference tables | `id`, `code` |
| Foreign key column | `<referenced_table>_id` (or `_code`) | `job_offer_id` |
| Index | `idx_<table>_<cols>` (full column name) | `idx_application_job_offer_id` |
| Unique index/constraint | `uq_<table>_<cols>` | `uq_skill_name` |
| Check constraint | `<table>_<subject>_valid` | `job_offer_salary_range_valid` |
| Trigger | `trg_<table>_<purpose>` | `trg_job_offer_set_audit` |

## Primary keys

- Default: surrogate **`id UUID PRIMARY KEY DEFAULT uuidv7()`**.
- **Reference tables** use a natural key instead (e.g. `code CHAR(2)`), because the value is
  stable, externally meaningful, and useful to read directly in foreign keys.
- **Junction tables** use a **composite PK** of the two foreign keys, which also enforces
  "link at most once" and provides the index for the owner-side lookup.

## Timestamps

- All timestamps are **`TIMESTAMPTZ`**, stored in **UTC**.
- `created_at` has column default **`now()`** and is immutable after insert.

## Audit columns (the standard audit block)

Rich tables (tables with editable properties) carry these five columns:

| Column | Type | Null | Set by | Meaning |
| ------ | ---- | ---- | ------ | ------- |
| `created_at` | `TIMESTAMPTZ` | NO | column default `now()` | row creation time, immutable |
| `created_by` | `UUID` | NO | application (acting user) on insert | who created the row |
| `updated_at` | `TIMESTAMPTZ` | YES | **trigger** on update | last modification time, `NULL` until first update |
| `updated_by` | `UUID` | YES | application / session variable on update | who last modified the row |
| `version` | `SMALLINT` | NO (default `1`) | **trigger** on update | optimistic-locking counter |

A table entry that uses this block unchanged is marked **`Audit: standard`** and omits these
rows from its column list.

`updated_by` is omitted on **single-updater** tables — tables where only the user who created a row
can ever edit it, so `updated_by` would always equal `created_by` and carries no information. Tables
that several users can edit keep `updated_by`, because the last editor is not implied by the creator.
Such a table is marked **`Audit: standard, without updated_by`** (it keeps `created_at`, `created_by`,
`updated_at`, `version`). The audit trigger is unaffected — it only ever sets `updated_at` and `version`.

## Optimistic locking (`version`)

Every update must carry the `version` the client last read. The `UPDATE ... WHERE id = :id
AND version = :version` returns 0 rows on a stale write. The [audit trigger](#audit-trigger-the-version--updated_at-rule)
increments `version` so the application never sets it directly.

## Audit trigger (the `version` / `updated_at` rule)

`updated_at` and `version` are maintained by a shared `BEFORE UPDATE` trigger so the
application can never forget them.

```sql
CREATE OR REPLACE FUNCTION set_audit_fields()
RETURNS trigger AS $$
BEGIN
    NEW.updated_at := now();
    NEW.version    := OLD.version + 1;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- applied per audited table:
CREATE TRIGGER trg_<table>_set_audit
    BEFORE UPDATE ON <schema>.<table>
    FOR EACH ROW EXECUTE FUNCTION set_audit_fields();
```

## Lean link tables vs. rich tables

A **pure junction** (a link with no editable attribute of its own) is insert/delete only —
it is never updated — so it omits `updated_at`, `updated_by`, and `version`, keeping only
`created_at` + `created_by`. A table entry states **`Audit: lean (created_at, created_by)`**.

If a junction carries a *mutable* attribute (e.g. `required_level`), it is **rich**: it gets the
full audit block and the update trigger.

## Enumerations: `CHECK` vs. reference table

| Use a `CHECK (col IN (...))` | Use a reference (lookup) table |
| ---------------------------- | ------------------------------ |
| Small, stable, app-controlled value set | Larger, externally-defined, or user-extensible set |
| No extra attributes per value | Values need attributes (name, regex, ordering, flags) |
| e.g. `status`, `action`, `employment_type`, `work_mode` | e.g. `country`, `language`, `currency`, `skill` |

A `CHECK` value set's codes are the Slovak terms as written, lowercase, diacritics and spaces included (`nová žiadosť`,
`motivačný list`); [ADR-D009](decision-log.md#adr-d009--new-value-sets-use-slovak-codes). `job_post`'s older value sets
keep their English codes.

## Cross-service / cross-module references (soft references)

A column that points at a row **owned by another module/service** (e.g. a `job_offer_id` stored
outside the `recruitment` module) is a **logical reference**: a plain `UUID`/code with **no database
foreign key**, because the referenced table may live in another database after extraction.

- Such columns are listed under **Foreign keys** marked **`logical — no DB FK`**.
- Referential integrity is the application's responsibility on write.
- Parent deletion does **not** cascade across the boundary. The owning module emits a
  **domain event** (e.g. `JobOfferDeleted`) that this module reacts to for cleanup.
- References **within the same module** use real foreign keys with an explicit `ON DELETE`.

## `ON DELETE` defaults

Choose deliberately and always state it:

- `CASCADE` — child has no meaning without its parent (e.g. `job_offer_skill` references `job_offer`).
- `RESTRICT` — parent must not disappear while referenced (e.g. any `*_id` referencing a reference/catalog row).
- `SET NULL` — link is optional and the row survives without it.

## Soft delete

Tables that must retain deleted rows (e.g. `file`) use **`deleted_at TIMESTAMPTZ NULL`** +
**`deleted_by UUID NULL`**, "alive" rows are those with `deleted_at IS NULL`. Reads must
filter on it.

## Case-insensitive uniqueness

Where a name must be unique regardless of case, use a unique index on **`lower(col)`**
(or a `citext` column). Stated explicitly per index.

## Search indexes

Free-text search columns (e.g. `job_offer.title`) use a **trigram GIN** index
(`pg_trgm`) rather than a plain b-tree, to support fuzzy / partial matching.

- One index per searched column, `USING gin (<col> public.gin_trgm_ops)`, named like any index
  (`idx_<table>_<col>`). A search across several columns joins them with `OR`, which the planner serves with a
  `BitmapOr` of the per-column indexes.
- The extension lives in `public` and is created once per database ([ADR-D006](decision-log.md#adr-d006--extensions-live-in-public-and-are-created-by-the-operator)),
  so the operator class is always written schema-qualified: a module schema's `search_path` need not include `public`.

## Outbox pattern (`outbox_message`)

Every producing module owns a **transactional outbox** table named `outbox_message` in its **own
schema** (`<module>.outbox_message`, e.g. `recruitment.outbox_message`). The shape and rules are
identical everywhere, so the definition lives here once instead of being repeated in each module. A
module section mentions its outbox only if it deviates.

**Purpose.** The application's change-capture interceptor writes one row per insert/update/delete of an
audited entity **in the same transaction as the change**, so the business change and its change-event
commit atomically (the transactional-outbox pattern). Modules also stage domain events and application
audit events here (see `kind`). A background dispatcher then drains the table, delivers each row to the
registered consumers (e.g. an audit log and opt-in handlers in other modules), and **deletes** the row on
success, so the table only ever holds the undelivered backlog. A crash before the delete commits
redelivers the row, so delivery is **at-least-once** and every consumer must be idempotent, keyed on `id`.

**Audit:** none (infrastructure queue — rows are inserted then deleted once delivered) · **Soft delete:** no

**Columns**

| Column | Type | Key | Null | Default | Description |
| ------ | ---- | --- | ---- | ------- | ----------- |
| `id` | `UUID` | PK | NO | `uuidv7()` | App-assigned v7 GUID, strictly increasing within the API process (`OutboxIds`), so it is the dispatch order; also the delivered change's id |
| `kind` | `TEXT` |  | NO | `'row_change'` | `row_change` = captured row mutation; `domain_event` = an event a module published for other modules; `audit_event` = an application audit event bound for the audit log (see notes) |
| `module` | `TEXT` |  | NO |  | Owning module/schema (e.g. `recruitment`) |
| `event` | `TEXT` |  | NO |  | The feature event that caused the row (the command's or worker's event name, or a published event's name); `unnamed` outside any command or worker. No module prefix |
| `entity_type` | `TEXT` |  | NO |  | The table/entity the row is about |
| `entity_id` | `TEXT` |  | NO |  | That entity's key as text (composite keys joined) |
| `action` | `TEXT` |  | NO |  | See CHECK; `insert` for published and audit events |
| `old_value` | `JSONB` |  | YES |  | Row before the change (`null` on insert and for events) |
| `new_value` | `JSONB` |  | YES |  | Row after the change (`null` on delete); a domain event's payload; an audit event's envelope |
| `actor_type` | `TEXT` |  | NO |  | Who caused it: `user` / `service` / `system` / `anonymous` / `unknown` |
| `actor_user_id` | `UUID` |  | YES |  | The acting user; set exactly for a `user` actor |
| `actor_service` | `TEXT` |  | YES |  | The acting service; set exactly for a `service` actor |
| `trace_id` | `TEXT` |  | YES |  | W3C trace id active when the row was written |
| `occurred_at` | `TIMESTAMPTZ` |  | NO | `now()` | When the change occurred |
| `version` | `SMALLINT` |  | NO | `0` | Entity's optimistic-locking version after the change (`0` for an entity without one) |
| `attempts` | `SMALLINT` |  | NO | `0` | Failed deliveries so far; the dispatcher stops picking the row up at 10 |
| `next_attempt_at` | `TIMESTAMPTZ` |  | YES |  | When a failed row may be tried again (exponential backoff, 5 s doubling up to 1 h); `null` for a row never tried |
| `last_error` | `TEXT` |  | YES |  | `Handler: ExceptionType` of the last failure; never the exception's message, which may carry personal data |

**Constraints**

| Name | Type | Definition | Purpose |
| ---- | ---- | ---------- | ------- |
| `outbox_message_kind_valid` | CHECK | `kind IN ('row_change','domain_event','audit_event')` | Guards the value set |
| `outbox_message_action_valid` | CHECK | `action IN ('insert','update','delete')` | Guards the action enum — an outbox row records exactly one row-mutation kind |
| `outbox_message_actor_type_valid` | CHECK | `actor_type IN ('user','service','system','anonymous','unknown')` | Guards the value set |
| `outbox_message_actor_valid` | CHECK | `(actor_type = 'user') = (actor_user_id IS NOT NULL) AND (actor_type = 'service') = (actor_service IS NOT NULL)` | A user always has an id, a service a name, nobody else either |
| `outbox_message_actor_user_id_not_empty` | CHECK | `actor_user_id <> '00000000-…'` | The all-zero UUID is never a user |
| `outbox_message_attempts_valid` | CHECK | `attempts >= 0` | No negative count |

**Indexes**

| Name | Columns | Type | Purpose |
| ---- | ------- | ---- | ------- |
| *(PK)* | `(id)` | btree | Dispatcher drains due rows in id (occurrence) order — no extra index needed; the table holds only the backlog |

**Notes**

- The outbox lives in the **producer's** database, not the consumer's. Today delivery is in-process.
  When a consumer becomes a separate service, only the dispatcher's transport changes
  (an in-process call becomes a message-broker publish).
- **Delivered rows are deleted**, so the table stays small (only the backlog). On a crash before the
  delete commits, the rows survive and are redelivered, so delivery is at-least-once and consumers must
  be idempotent (keyed on `id`).
- **A failing row does not hold up the rest.** When a batch fails, the dispatcher delivers its rows one by one: the
  rows that go through are deleted, and each failing one records `attempts`, `last_error` and `next_attempt_at`. After
  10 attempts the row stays for an operator and logs `outbox.message.gave_up`. A later row may therefore be delivered
  before an earlier one that failed.
- **Only what EF Core tracks is captured.** A row deleted by `ON DELETE CASCADE`, or written with raw SQL,
  `ExecuteUpdate` or `ExecuteDelete`, produces no change; a feature doing that publishes a domain event in the same
  transaction when a consumer depends on it (e.g. `files_released`).
- **Today one module produces.** `recruitment.outbox_message` carries the changes of `application_document`, for the
  Files module. Marking another entity `ITrackedEntity` captures it too.
- **Attribution is captured at write time**, because the dispatcher runs later on a background thread with
  no request to read. The interceptor stamps the current actor (a worker's scope, else the request's user,
  else `anonymous`; `unknown` with neither), the trace id, and the ambient feature event onto every row of
  the save — including events staged in it. A missing actor is `unknown`, never a sentinel id. The actor is
  `user` for a staff token, `anonymous` for a request without one (an applicant), and `system` outside any request.
- **`kind` — events on the same outbox.** The change-capture interceptor can only express *row*
  mutations, but some facts a module needs to broadcast are not a single row change — e.g. "a job offer's
  application deadline has passed" (a condition only the owning module can evaluate). Rather than adding
  a second messaging mechanism, a module **publishes an event** by inserting an outbox row directly (in
  its own unit of work, so it stays atomic with the state change that justified it) with
  `kind = 'domain_event'`, `event` = the event name, `entity_type`/`entity_id` = what it is about, and
  `new_value` = the payload JSON. An **application audit event** that must commit with a change is staged
  the same way with `kind = 'audit_event'`. All kinds drain, deliver and delete exactly like captured
  changes. The difference is who consumes them: an audit log takes `row_change` and `audit_event`; opt-in
  handlers subscribe to row changes by `(module, entity_type, action)` or to domain events by
  `(module, event)`. This lets another module react to `recruitment` / `job_offer_closed` without the
  Recruitment module knowing anything about its consumers.

---

<!-- markdownlint-disable-file MD036 -->
