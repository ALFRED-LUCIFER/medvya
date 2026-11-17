# ADR-024 · Patient data requests and erasure: crypto-shredding, an ids-only ledger, retention jobs

- **Status:** Proposed 16 Nov 2026. Decision owners: the founder and counsel (MDV-P1-044). Nothing here is built; until it is, requests follow the manual process in [data-subject-requests.md](../runbooks/data-subject-requests.md). Tickets: MDV-P3-019 (audio purge), MDV-P5-025 (retention jobs and patient requests).
- **Builds on:** [ADR-006](ADR-006-immutable-signed-records.md) (signed records are never edited), [ADR-012](ADR-012-postgres.md) (hash-chained ledger, per-tenant envelope encryption of national IDs), [retention map](../compliance/retention-map.md).

## Context

India's DPDP Act 2023 (with the DPDP Rules 2025) gives a data principal the right to access, correct and erase their personal data, and expects data to be deleted once its purpose is served unless a law requires keeping it. The UAE health data law (Federal Law No. 2 of 2019) and emirate rules require medical records to be kept for long periods. Medvya is the clinic's processor: the clinic decides, Medvya executes.

Today nothing enforces retention and nothing handles a request. Two parts of the design conflict with erasure:

1. The audit ledger is append-only and hash-chained (ADR-012), with daily digests in immutable Blob. A row cannot be deleted without breaking the chain.
2. Backups keep deleted rows for the point-in-time window (35 days in production).

## Decision (proposed)

1. **Crypto-shredding for erasure.** PHI columns that may need erasure are encrypted by the application under a per-patient data key, itself wrapped by the tenant key in Key Vault (the same envelope as national IDs, ADR-012, one level deeper). Erasing a patient destroys their data key: the rows stay, so foreign keys and chains hold, but the values are unreadable everywhere, including in backups once the key is gone from the key store's own retention.
2. **The ledger keeps ids only.** Audit rows and digests carry opaque ids, action codes and hashes, never names, numbers or free text. Erasure therefore never touches the ledger; an `erasure` event records who erased which patient id and why.
3. **Medical records under a retention rule are held, not erased.** An erasure request on a record still inside its legal retention period is answered "partially fulfilled, legal hold", with the date the hold ends; the retention job erases it then.
4. **Retention runs as scheduled jobs**: audio after 30 days (MDV-P3-019), every other class per country and per the retention map (MDV-P5-025), each deletion logged with counts and id ranges only. A legal hold always blocks deletion.
5. **Requests are tracked** with due dates per country and a receipt to the clinic (MDV-P5-025); until that is built, by hand (runbook above).

## Consequences

- Every new PHI column must be classified for erasure (crypto-shredded, held, or deleted by a job) when it is added.
- Search over crypto-shredded columns needs blind indexes, as national IDs have today.
- Key deletion must itself be audited and irreversible; restores from backup must not bring a destroyed key back (the key store, not the database backup, is the source of truth).
- Open for counsel: retention periods per class and country, the response deadlines, whether the clinic or Medvya answers the principal, and whether crypto-shredding counts as erasure under each law.
