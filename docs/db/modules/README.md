# Modules

Each module owns a separate schema (a bounded context). Table documentation is grouped by module.

- [Kernel](kernel.md) — host-owned infrastructure: HTTP idempotency records
- [Files](files.md) — metadata and lifecycle of every uploaded file; the bytes are in Blob Storage
- [Recruitment](recruitment.md) — job posts; staff are referenced by their Entra object id
