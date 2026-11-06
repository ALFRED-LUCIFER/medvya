# ADR-006 · Immutable signed records and tamper evidence

- **Status:** Accepted 6 Nov 2026 (founder). (P0 foundation, P2 notes). **Mechanism amended 6 Nov 2026:** SQL ledger tables are replaced by a hash chain, append-only triggers and daily Blob digests, see [ADR-012](ADR-012-postgres.md).

## Decision

- Audit events, signed note versions, consent receipts and issued invoices are stored in **hash-chained, append-only PostgreSQL tables** (per-tenant `PrevHash`/`RowHash` filled by a trigger under an advisory lock; UPDATE, DELETE and TRUNCATE refused for every role; `LedgerSql.AddLedger` in a migration opts a table in).
- A daily job writes an HMAC-signed digest of every chain head to an immutable Blob container, and a scheduled job re-reads the chains and alerts on mismatch (`LedgerDigests`). A superuser can disable triggers; the chain check and the digest are what detect it.
- A signed note is never updated. Corrections are signed **addenda** (new versions with `amends_version_id` and a reason).
- The Rx PDF is rendered server-side and stored with its hash.
- Signing is an authenticated action (passkey/MFA re-check). A signature image only appears on print.

## Consequences

- The app database user has no UPDATE/DELETE on signed versions.
- An automated test proves that a signed note cannot be edited (P2 exit gate).
- Document hashes and signing events show integrity. They are **not** marketed as legally qualified digital signatures unless local requirements are met.
