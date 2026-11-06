# ADR-006 · Immutable signed records and tamper evidence

- **Status:** Accepted 6 Nov 2026 (founder). (P0 foundation, P2 notes). **Mechanism amended 6 Nov 2026:** SQL ledger tables are replaced by a hash chain, append-only triggers and daily Blob digests, see [ADR-012](ADR-012-postgres.md).

## Decision

- Audit events, signed note versions, consent receipts and issued invoices are stored in **Azure SQL ledger tables** (append-only where applicable).
- Ledger digests are exported to immutable Blob storage and verified on a schedule.
- A signed note is never updated. Corrections are signed **addenda** (new versions with `amends_version_id` and a reason).
- The Rx PDF is rendered server-side and stored with its hash.
- Signing is an authenticated action (passkey/MFA re-check). A signature image only appears on print.

## Consequences

- The app database user has no UPDATE/DELETE on signed versions.
- An automated test proves that a signed note cannot be edited (P2 exit gate).
- Document hashes and signing events show integrity. They are **not** marketed as legally qualified digital signatures unless local requirements are met.
