# ADR-012 · Database: PostgreSQL with EF Core, application-level encryption of national IDs

- **Status:** Accepted, 6 Nov 2026 (decider: founder). Supersedes the database choice in ADR-001, the mechanism in ADR-005 and the ledger mechanism in ADR-006. The intent of ADR-003, 004, 005 and 006 is unchanged.
- **Tickets:** MDV-P0-031 to P0-039.

## Context

ADR-001 chose Azure SQL for Always Encrypted, ledger tables, dynamic masking and row-level security on `SESSION_CONTEXT`. The founder wants PostgreSQL now, before real data exists, for cost, portability and one open engine for the long run, and still wants national IDs unreadable by database administrators. EF Core stays.

## Decision

| Concern | Choice |
|---|---|
| Engine | **Azure Database for PostgreSQL Flexible Server**, one per country (Central/South India, UAE North), private endpoint only, Entra-only authentication, customer-managed key, zone-redundant HA and point-in-time restore kept in-country |
| Access | **EF Core 10 on Npgsql** (`Npgsql.EntityFrameworkCore.PostgreSQL`), one schema per module, one baseline migration per module, forward-only afterwards |
| Tenant isolation | Layer 1: EF global query filters (unchanged). Layer 2: `FORCE ROW LEVEL SECURITY` with policies on `current_setting('medvya.tenant_id', true)`, set per connection by the existing interceptor and reset when the connection returns to the pool. The app role has no BYPASSRLS, no DDL and no superuser |
| National and insurance IDs | Encrypted **in the application**: AES-256-GCM with a per-tenant data key wrapped by an Azure Key Vault key. Exact lookup uses an HMAC-SHA-256 blind index with its own Key Vault key. A database admin sees only ciphertext because the database never holds an unwrapped key. Replaces Always Encrypted |
| Tamper evidence | Per-tenant **hash chain** on audit events, signed note versions, consent receipts and issued invoices; triggers refuse UPDATE and DELETE for the app roles; a daily signed digest goes to immutable Blob; a scheduled job verifies the chain. Replaces SQL ledger tables. This is integrity evidence, not a qualified digital signature |
| Support and reporting | Role with read access only to masked views, no unwrap right. Replaces dynamic masking |
| Jobs and tests | Hangfire.PostgreSql, Testcontainers.PostgreSql |

## Alternatives rejected

- **Stay on Azure SQL:** keeps Always Encrypted and ledger as managed features, but the founder chose an open engine now, while rework is cheapest.
- **Drop EF Core for plain SQL or another ORM:** a rewrite of every module for no gain the product needs. Rejected by the founder.
- **Cosmos DB:** no relational integrity or row-level security for this model.

## Consequences

- Always Encrypted and ledger tables become application code, so they need the P1 security review's attention (MDV-P1-038).
- A merge freeze on schema-changing PRs covers MDV-P0-033 (one baseline migration; no real data is lost because none exists).
- `docs/03-architecture/database-security.md`, `infra/sql/roles.sql` and the data map are rewritten under MDV-P0-034 to P0-037.
- Load budgets from MDV-P1-037 are re-measured (MDV-P0-039).
