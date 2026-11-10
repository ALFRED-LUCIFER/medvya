# Desk patient search: how it works, what it costs, where it stops

Front-desk search (`POST /v1/patients/search`, `PatientRegistry.SearchAsync`, MDV-P1-018) must stay under 300 ms at p95 and forgive the spellings Indian names come in. This note records the design, the measurements behind it and the ceiling. The seeding tool that produced the data is in [load-testing.md](../05-delivery/load-testing.md).

## Design

| Query | How it is found | Index |
|---|---|---|
| MRN | exact on the unique `(TenantId, Mrn)` index; the survivors of merged records with that MRN are looked up first, by the same index | yes |
| Phone | exact `Mobile` or `AlternatePhone` | `Mobile` index; the alternate number is filtered |
| 4 to 15 digits (not a full phone, ABHA or Emirates ID) | an MRN first (6 digits or fewer: the MRN index, merged records to their survivor); only with no MRN hit, the digits as part of `Mobile` or `AlternatePhone` (`LIKE`, a filter on the organisation's rows, never an index) or as an Aadhaar last 4 (blind index). A typed 12-digit Aadhaar is cut to its last 4 in memory; the rest is dropped, never stored or logged | MRN index, then a filter |
| ABHA, Emirates ID | HMAC blind index (ADR-012); the value is never compared in plaintext | yes |
| Name | the stored **search key** (below): every word is a substring of it (`LIKE`); ranked words-together-at-a-word-start first, then trigram similarity, then A to Z | tenant index, then a filter |
| Name, fewer than 3 exact hits | the same, but a word may also be near a word of the key (`word_similarity` of at least 0.6) or have two adjacent letters swapped (`krsihnan` finds `Krishnan`). Exact hits still rank first. One pass, at most 1,000 matches (the paging limit) | filter |

**Search key.** `patients."Patients"."SearchKey"` is a stored generated column, `patients.search_key("FullName", "ArabicName")`, kept by the database on every insert and update, so no code path can forget it and a bulk load fills it too. `patients.fold_name` lower-cases and folds spellings that meet in practice: `Mohamed`, `Mohammed`, `Muhammad` give `muhamad`; `Krishnann` gives `krisnan`; `Shree` and `Sree` give `sri`; `D'Souza` gives `dsuza`; `Laxmi` and `Lakshmi` give `laksmi`; `Fathima` and `Fatima` meet. Devanagari and Arabic letters pass through lower-cased, so those names are found by substring. The query is folded by the same SQL function (never a second copy in C#), then cut into letter-and-digit words, so a `%`, `_` or regex character in what the user typed can never reach a pattern.

**What is not searchable.** National IDs: the key is made from the two name columns only. Search logging: the endpoint's audit event carries ids and the action, never the query text (unchanged); no new logging was added.

**Tenant isolation.** Unchanged and two-layered: the EF query filter and row-level security. The search adds filters on rows both already restrict.

## Why there is no trigram index

A `pg_trgm` GIN index was built and measured first (a composite on `TenantId, SearchKey gin_trgm_ops`). As a superuser it answers in 3 ms to 9 ms. As `medvya_app` PostgreSQL never uses it for `LIKE`, `%` or `<%`: with row-level security, a user's qualifier may reach an index only if its function is **leakproof**, and `textlike`, `similarity_op` and `word_similarity_op` are not (`SELECT proname, proleakproof FROM pg_proc`). Only a superuser can mark a function leakproof, which Azure Flexible Server does not give us. So the index would be 10 MB per 240,000 patients and used by nothing. The planner uses the tenant index, and the filter runs on that organisation's rows.

Ways around it, none taken: a `SECURITY DEFINER` search function under a role exempt from the policy (the tenant predicate would then rest on one function, not on row-level security: a new ADR); a token table searched by prefix with leakproof comparisons (`text_pattern_ops`), which cannot do middle-of-word or typos without storing every deletion variant; an external search service (ruled out). The extension is still needed for `similarity` and `word_similarity` (functions only): `roles.sql` creates `pg_trgm`, and `azure.extensions` lists `PG_TRGM` (`infra/bicep/postgres.bicep`).

## Measurements

Local PostgreSQL 16 in Colima, one practice per size in the same table, seeded by `tools/synthetic-data-generator`, row-level security on (as `medvya_app`), `EXPLAIN ANALYZE` execution time, p50 / p95 over 5 to 15 runs. A laptop: use it for shape, not for Azure latency.

| Query | 40,000 | 200,000 | 1,000,000 |
|---|---|---|---|
| Before: name, page (substring `ILIKE` on name, ordered by name) | 15 ms (typo: 0 rows) | 110 ms | not run |
| Before: name, count | 15 ms | 60 ms | not run |
| After: name with exact hits, count and page | 5 and 6 ms | 30 and 34 ms | 122 and 135 ms |
| After: typo, one pass (fuzzy and swapped letters) | 54 ms | 274 ms | 631 ms |
| MRN before (correlated `EXISTS` in an `OR`) | 7 ms | 23 to 65 ms | 913 ms |
| MRN after | 0.02 ms | 0.02 ms | 0.02 ms |
| Phone exact | 4 ms | 23 ms | 42 ms |

Plans: name with exact hits, tenant index (bitmap) at 40k and 200k, parallel sequential scan at 1,000,000, the substring filter on each row; typo the same with the similarity filter (about 1 µs per row, CPU bound); MRN a `BitmapOr` of the MRN index and the primary key; phone a bitmap scan on the `Mobile` index plus filter.

Through the endpoint (`PatientSearchPerformanceTests`, Testcontainers PostgreSQL, 88 searches: prefix, spelling variant, transposed and missing letters, two-word partial, phone, MRN, ABHA, directory): 40,000 patients p50 17 ms, p95 77 to 134 ms (limit 300). At 200,000 (`MEDVYA_SEARCH_PATIENTS=200000`): p50 111 ms, p95 480 ms, so the typo queries are over budget there and the exact ones are not.

## Ceiling and what to do at the ceiling

- Exact-hit name search, MRN, phone and identifier: comfortable to 1,000,000 patients in one organisation.
- Typo-tolerant fallback: within budget to about 100,000 patients per organisation on this hardware; at 200,000 it is about 0.3 s in the database alone. An organisation that big needs the leakproof token table or the `SECURITY DEFINER` function above (an ADR first). Since MDV-P1-072 patients are searched per organisation (tenant), across all its locations, so the total of every location counts.
- A word shorter than 3 characters matches too much to be useful and still runs.
- `AlternatePhone` has no index; add one (`TenantId, AlternatePhone`) if phone search at 1,000,000 matters.
- Changing the folding rules rewrites the table: ship a new function and column, never an edit in place.
- No unaccent: Latin names with diacritics are not folded.

## Tests

`PatientNameSearchTests` (variants, doubled, missing and transposed letters, two-word partial, Arabic, Devanagari, ranking, another tenant and facility never shown), `PatientSearchPerformanceTests` (40,000 patients, p95 limit), `PatientRegistryTests` and the merge tests (exact search and merged MRNs).
