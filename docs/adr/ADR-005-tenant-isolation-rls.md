# ADR-005 · Tenant isolation: EF Core filters + PostgreSQL row-level security

- **Status:** Accepted 6 Nov 2026 (founder). (P0). **Mechanism amended 6 Nov 2026:** row-level security is PostgreSQL `FORCE ROW LEVEL SECURITY` with `current_setting`, see [ADR-012](ADR-012-postgres.md); the two-layer rule is unchanged. Built in MDV-P0-034.

## Decision

- Every clinic-owned table has `tenant_id` (`TenantId`; `tenancy.Tenants` uses `Id`).
- The application sets the session setting `medvya.tenant_id` on each connection open, via a DbConnection interceptor, from the authenticated membership (`set_config(..., false)`; Npgsql runs `DISCARD ALL` when a connection returns to the pool, so the next tenant never inherits it).
- Each such table has `ENABLE` and `FORCE ROW LEVEL SECURITY` (so the table owner is bound too) and four policies, `tenant_select`, `tenant_insert`, `tenant_update`, `tenant_delete`, all comparing the tenant column with `nullif(current_setting('medvya.tenant_id', true), '')::uuid`. A connection with no tenant sees and writes nothing. The policies come from one helper, `RowLevelSecurity.Tenant` (BuildingBlocks.Infrastructure), called from a forward-only EF migration; an integration test fails if a table with a `TenantId` has no forced policy.
- The roles that run the application are `NOSUPERUSER NOBYPASSRLS` and own nothing (`infra/postgres/roles.sql`).
- EF Core global query filters apply the same rule in the application (defence in depth).
- Facility scoping and care relationship are enforced in application policies.

## Implementation note (MDV-P0-012)

The facility picker must list one person's memberships across tenants. A dedicated connection sets `medvya.user_id` with no tenant; the SELECT (and UPDATE-visibility) policies on `Tenants`, `Facilities` and `Memberships` then also admit that user's `Active` memberships and their tenants and facilities. The extra branch applies only when no tenant is set, the `WITH CHECK` of every write is unchanged (so such a connection writes nothing, and an UPDATE of a row seen this way fails), and other tables (feature flags, patients, audit) are untouched. See docs/03-architecture/database-security.md.

## Consequences

- An automated test proves that a cross-tenant read and write are blocked by RLS **even when the EF filter is bypassed**, and another that the EF filter alone still hides other tenants (P0 exit gate).
- Reporting and support roles use masked views (MDV-P0-037).
- Migrations run as a separate database login.
- Known limit: the tenant is a session setting any connection may set, so the protection is against application bugs and a stolen app query, not against a principal that can run arbitrary SQL as the app role. The app role is therefore the only login with module write rights, and it has no DDL.
