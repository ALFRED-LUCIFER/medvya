# ADR-005 · Tenant isolation: EF Core filters + Azure SQL RLS

- **Status:** Proposed (P0)

## Decision

- Every clinic-owned table has `tenant_id`.
- The application sets `SESSION_CONTEXT(N'tenant_id')` (read-only) on each connection open, via a DbConnection interceptor, from the authenticated membership.
- An Azure SQL security policy applies FILTER and BLOCK predicates on `tenant_id`.
- EF Core global query filters apply the same rule in the application (defence in depth).
- Facility scoping and care relationship are enforced in application policies.

## Consequences

- An automated test proves that a cross-tenant read and write are blocked by RLS **even when the EF filter is bypassed** (P0 exit gate).
- Reporting and support roles use masked views.
- Migrations run as a separate database user.
- Connection pooling must reset the session context. Use `sp_set_session_context` with `@read_only = 1` on every open.
