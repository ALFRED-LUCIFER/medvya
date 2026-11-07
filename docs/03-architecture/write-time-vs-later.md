# Documents and billing: what to keep current at write time, what to compute later

A design note for the two modules after patients (#284). Nothing here is built. It answers one question: for each search or total we will want, is it kept up to date when the row is written, or worked out later, and what must exist in the schema now so adding it later is a plain migration and not a rewrite or a risky backfill. Evidence comes from patient search (measured at 40,000, 200,000 and 1,000,000 patients, see [patient-search.md](patient-search.md)).

## The rules, from what the patient work showed

1. **Keep at write time what a screen needs in under 300 ms and what is cheap to maintain.** Use a stored generated column when it depends only on the same row (the patient `SearchKey`: 13 bytes a row, filled by the database on every insert, so a bulk load and every code path get it free). Use a trigger or the write transaction when it depends on other rows (a running balance).
2. **Compute later what is a report**: totals by month, ageing, practice-wide counts, rebuilds. A background job, rebuildable from the source rows, never the only copy of a fact.
3. **Under row-level security a trigram or full-text index on a user-supplied operator is not usable**, because `LIKE`, `%`, `<%` and `@@` are not leakproof and only a superuser can change that. Patient search found this the hard way (a built GIN index was never chosen as `medvya_app`). So plan for a filter over one facility's rows (cheap to about 100,000 rows with a similarity filter, about 1,000,000 with a plain substring filter) or for equality and prefix lookups, which are leakproof (`text_pattern_ops`, `uuid` and `bigint` equality). A text search over documents that must be faster than that needs an ADR first: a token table searched by prefix, or a `SECURITY DEFINER` function whose tenant predicate stands outside row-level security.
4. **Folding rules are forever.** Changing a generated column's expression rewrites the table. Ship a new column and function, backfill in batches, switch reads, drop the old one.
5. **PHI stays out of indexes, logs and URLs.** An index over a derived value (a folded name, extracted text) is the same class as its source, so it lives only in the database and is covered by the same row-level security. Never index a national ID, insurance member number or free clinical text in plaintext; exact lookup of an identifier goes through its HMAC blind index. Search terms are never logged; the audit event records the action and ids.

## Documents (uploads, extracted text, per-patient timeline)

| Thing | When | How | Why |
|---|---|---|---|
| Document row: tenant, facility, patient id, kind, uploaded by and at, blob key, content hash, size, MIME type, page count, status | write | plain columns | the timeline is `WHERE PatientId = x ORDER BY UploadedAt DESC`: one btree on `(TenantId, FacilityId, PatientId, UploadedAt DESC)` |
| Timeline entry ("a document was added") | write | the document row itself is the entry; clinical events keep their own tables and a read merges by time | no second copy to drift |
| Title and kind search | write | a folded `SearchKey` generated column like patients' | cheap, same pattern, same filter-over-one-patient cost (a patient has dozens of documents, not thousands) |
| Extracted text (OCR, PDF text) | later | a background job fills `ExtractedText` and `ExtractionStatus`; a nullable text column and a status column exist from day one | extraction takes seconds and can fail; the upload must not wait or fail on it |
| Full-text search over extracted text | later, per patient first | `tsvector` generated column over `ExtractedText`, filtered within one patient's documents (dozens of rows: no index needed); practice-wide text search is a separate decision under rule 3 | the leakproof limit makes a practice-wide `@@` index unusable under RLS |
| Per-patient counts, last upload | read time | `COUNT` and `MAX` on the timeline index | cheap per patient; no counter to keep right |
| Retention and legal hold | write | `RetainUntil` and `LegalHold` columns at upload | a backfill of retention dates later is the risky kind |
| Hash chain | write | the issued-document and signed-note rows join the existing per-tenant chain (ADR-006, ADR-012) | cannot be added to old rows afterwards without breaking the chain |

Blob content lives in Blob, never in the database; the row holds the key and the SHA-256. Blob names are random ids, never a patient name. The extraction job's logs carry ids and counts only.

Add now: the document table with the columns above including `ExtractedText` (null), `ExtractionStatus`, `RetainUntil`, `LegalHold`, `ContentHash`; the timeline index; row-level security through the same `RowLevelSecurity.Tenant` helper in its first migration (a table without it is refused by test).

## Billing (invoices, payments, per-patient balances)

| Thing | When | How | Why |
|---|---|---|---|
| Invoice, line items, payment: integer minor units plus ISO currency | write | plain columns (AGENTS.md 3.4) | never `decimal` |
| Legal number series | write | the existing `DocSequences` that only move forward | a number cannot be re-issued; the series already refuses delete and backward moves |
| Invoice and payment chain | write | issued invoices join the hash chain; a correction is a credit note, never an edit | tamper evidence cannot be added later |
| Per-patient balance | write, in the same transaction | a `PatientBalances` row `(TenantId, PatientId, Currency, BalanceMinor, LastEntryId)` updated by the invoice or payment insert; each entry also stores `BalanceAfterMinor` | the front desk shows the balance next to the name on every search result; summing all entries per patient at 1,000,000 patients is too slow to do per request |
| The balance's proof | later | a nightly job recomputes from entries and compares; a mismatch opens an alert and never auto-corrects | the entries are the truth, the balance is a cache |
| Daily takings, ageing buckets, monthly revenue, tax summaries | later | a job into report tables, or the masked reporting views | reports; rebuildable from entries |
| Search invoices by number or by patient | write | btrees on `(TenantId, FacilityId, Number)` and `(TenantId, PatientId, IssuedAt DESC)` | equality and range are leakproof; no text search needed |
| Payer name, plan, member number | write | plain, member number encrypted with its blind index as in patients | an identifier-class value |

Add now: an append-only ledger-style entry table with a signed minor-unit amount and currency, the entry-to-invoice link, the `BalanceAfterMinor` column, and `PatientBalances`, even if the first release only writes invoices. A balance introduced later has to be backfilled from every entry ever written, per tenant, under live traffic; a column that exists from the start is filled by the first entry.

## How row-level security applies

Every new table carries `TenantId` and the tenant policy from its first migration, and `FacilityId` where the data is per facility. Every index starts with `(TenantId, FacilityId, …)` so the policy's own qualifiers and the facility filter are index conditions (leakproof equality) and the rest narrows from there. Any search over text is a filter over those rows, sized by rule 3. A reporting job reads through the masked views (`reporting` schema) or runs as a role that sets the tenant setting per tenant in a loop, never as a role that bypasses row-level security.

## Sizing (patients measured; documents and billing are estimates)

Measured on PostgreSQL 16 with the synthetic practice seeder: the `Patients` table with all its indexes is about 0.73 KB a patient (heap 230 bytes, the search key 13 bytes of that).

| Patients | `Patients` with indexes | Payer and contact rows (about 0.9 of a patient each) | Name search, exact hits (count + page) | Typo pass |
|---|---|---|---|---|
| 40,000 | about 29 MB | about 10 MB | 11 ms | 54 ms |
| 200,000 | about 146 MB | about 50 MB | 64 ms | 274 ms |
| 1,000,000 | about 730 MB | about 250 MB | 257 ms | 631 ms (over budget) |

Estimates to confirm when built (rows per patient are assumptions: 5 documents and 8 invoice or payment entries over the first years): documents 200,000 / 1,000,000 / 5,000,000 rows, about 0.5 KB a row with the timeline index (100 MB / 500 MB / 2.5 GB) before extracted text, which is the real cost (tens of KB a scanned page of text: plan for it to dominate, and keep it in a separate table if it grows past the heap size of the rest); billing entries 320,000 / 1,600,000 / 8,000,000 rows, about 0.3 KB a row with two indexes (100 MB / 500 MB / 2.4 GB); `PatientBalances` about 0.1 KB a patient (4 MB / 20 MB / 100 MB). None of these is a search problem; they are a storage and vacuum problem, handled by partitioning entries by year once a table passes about 10,000,000 rows.

## What to add to the schema now (checklist)

- Documents: `ExtractedText` (null), `ExtractionStatus`, `RetainUntil`, `LegalHold`, `ContentHash`, the timeline index, tenant policy, hash-chain registration.
- Billing: signed minor-unit entries with currency, `BalanceAfterMinor`, `PatientBalances`, number series, credit-note link, chain registration.
- Both: `(TenantId, FacilityId, …)` index prefixes, `CreatedAt` and `ChangedAt` in UTC, `ClassificationAttribute` on every property, no free-text PHI in any column that is indexed or logged.
- Not now: tsvector or trigram indexes (unusable under row-level security), an external search service (ruled out), report tables (computed later, from the entries).
