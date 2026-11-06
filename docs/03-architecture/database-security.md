# Database roles and row-level security (MDV-P0-008, rebuilt on PostgreSQL in MDV-P0-034)

Who can do what inside a Medvya database. The roles are defined in one idempotent script, [`infra/postgres/roles.sql`](../../infra/postgres/roles.sql), which is the single source of truth. Row-level security is written by one helper, `RowLevelSecurity.Tenant` (`src/BuildingBlocks/Medvya.BuildingBlocks.Infrastructure`), from a forward-only `TenantRls` migration in each module. Tests: `DatabaseRoleTests`, `TenantIsolationTests`, `MembershipDirectoryTests`, `RowLevelSecurityTests` in `tests/Medvya.IntegrationTests`. Decisions: [ADR-005](../adr/ADR-005-tenant-isolation-rls.md), [ADR-012](../adr/ADR-012-postgres.md).

National-ID encryption (MDV-P0-035) and masked reporting views (MDV-P0-037) are separate tickets and are not described here until built. The hash chain on append-only tables (MDV-P0-036) is described in ADR-006.

## Roles

Every role is `NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION` and `NOLOGIN`. A login (an Entra identity in Azure, a password role in development and tests) is created `IN ROLE` one of them.

| Role | Rights | Never |
|---|---|---|
| `medvya_owner` | Owns every module schema and table; runs DDL. Foreign-key checks run as the owner, so it keeps its data rights | Logins. Nobody connects as it |
| `medvya_migrator` | `USAGE, CREATE` on the module schemas; member of `medvya_owner` with `SET` but not `INHERIT`, so `db-migrate` does `SET ROLE medvya_owner` for the length of a migration | Read or write module data in a plain session (`MigratorCanChangeTheSchemaButNotTouchModuleData`) |
| `medvya_app` | Member of every module role below, nothing else | Any DDL, ownership of any object, disabling a policy or trigger |
| `medvya_tenancy` | `SELECT/INSERT/UPDATE/DELETE` on schema `tenancy` | Any other schema |
| `medvya_identity` | `SELECT/INSERT/UPDATE` on `identity` | `DELETE` (offboarding is a membership status, a session is revoked not removed) |
| `medvya_patients` | `SELECT/INSERT/UPDATE/DELETE` on `patients` | Any other schema |
| `medvya_billing` | `SELECT/INSERT/UPDATE` on `billing` | `DELETE` (it would restart a legal number series); changing `TR_DocSequences_OnlyMoveForward` (only the owner can) |
| `medvya_scheduling` | `SELECT/INSERT/UPDATE/DELETE` on `scheduling` | Any other schema; reporting has no access (appointments hold the SensitiveHealth visit reason) |
| `medvya_notifications` | `SELECT/INSERT/UPDATE` on `notifications` | `DELETE` (attempts are the delivery history); patients and scheduling data |
| `medvya_leads` | `SELECT/INSERT` on `leads` | `UPDATE`, `DELETE` |
| `medvya_audit` | `SELECT/INSERT` on `audit` | `UPDATE`, `DELETE` (the append-only triggers refuse them for every role too, MDV-P0-036) |
| `medvya_jobs` | `SELECT/INSERT/UPDATE/DELETE` on `hangfire` (job storage) | Any module's data. Hangfire's tables are created by the deploy (as the owner); the app only works in them |
| `medvya_reporting` | `SELECT` on `tenancy` | Any write, any DDL, `patients`, `audit`, `identity` (until MDV-P0-037 adds masked views) |

Default privileges (`ALTER DEFAULT PRIVILEGES FOR ROLE medvya_owner IN SCHEMA …`) give every module role its rights on tables and sequences the owner creates later, so a new table never needs a grant.

## Row-level security

- **Every clinic-owned table** (a `TenantId` column; `tenancy.Tenants` uses `Id`) is `ENABLE`d and `FORCE`d, with four policies (`tenant_select`, `tenant_insert`, `tenant_update`, `tenant_delete`) comparing the tenant column with `nullif(current_setting('medvya.tenant_id', true), '')::uuid`. `FORCE` binds the owner as well. A connection with no tenant matches no row, for reads and writes alike.
- **The tenant is set per connection** by `TenantSessionInterceptor` (`set_config('medvya.tenant_id', …, false)`). Npgsql resets a connection (`DISCARD ALL`) when it returns to the pool, so a pooled connection never carries another tenant (`PooledConnectionsNeverLeakTenantContext`).
- **The one cross-tenant read (facility picker, MDV-P0-012).** A connection that sets `medvya.user_id` and **no** tenant (only `MembershipDirectory` does) sees that user's `Active` memberships and the tenants and facilities they belong to. Only the SELECT and the UPDATE-visibility conditions have this branch, and it applies only while no tenant is set. Every `WITH CHECK` and the DELETE policy are tenant-only, so such a connection writes nothing (`MembershipDirectoryTests`).
- **Tables with a `TenantId` but no policy** are listed in `RowLevelSecurityTests.IdsOnly`: `tenancy.TenantIndex` and `scheduling.OutboxTenants` (tenant ids only, read by jobs that run with no tenant) and `identity.Sessions` (looked up by token before a tenant is known). The test fails for any other table, so a new table cannot be forgotten.
- **Adding a table** with a tenant column: call `migrationBuilder.Tenant("schema", "Table")` in its migration. **Adding a module:** add its schema and a row to `module_grants` in `roles.sql`, a `TenantRls` migration, and a cross-schema test in `DatabaseRoleTests`.
- **EF query filters stay as layer one**; RLS is layer two. `TheQueryFilterAloneStillHidesOtherTenants` (RLS out of the way) and `RowLevelSecurityHidesOtherTenantsEvenWithQueryFiltersOff` (filter out of the way) prove each alone.

## Order of operations

1. The administrator runs `roles.sql` (`dotnet run --project tools/db-migrate -- admin "<admin connection>"`; every deploy, idempotent, also covers new modules). For development add `medvya_app=<password> medvya_migrator=<password>` to create password logins.
2. The migrator login runs `db-migrate migrate "<migrator connection>"` (module migrations, then the Hangfire tables, all after `SET ROLE medvya_owner`).
3. The application starts as a login that is a member of `medvya_app`. Never as a superuser or the owner.

The integration fixture does exactly this: `roles.sql` as the container superuser, migrations as `medvya_migrator`, every context as `medvya_app`. A migration that needs more than the owner role fails the whole suite; an application query that needs more than the module roles fails the test that runs it.

## Mapping to Azure (MDV-P0-038, not built yet)

Azure Database for PostgreSQL provisioning, Entra-only authentication and the deploy workflow are MDV-P0-038. The shape is: the administrator (Entra admin group, just-in-time) runs `roles.sql`; the App Service managed identity is created as a role `IN ROLE medvya_app`; the deploy identity `IN ROLE medvya_migrator`; reporting users `IN ROLE medvya_reporting`. No password exists anywhere.

## Known limits

- The tenant is a session setting any connection can set (`set_config`), so RLS protects against application bugs and injected queries that stay inside the app's statements, not against someone who can run arbitrary SQL as the app role. The app login is therefore held only by the application identity.
- The migrator can `SET ROLE medvya_owner` and then read data; the split removes standing access, not the ability. The deploy identity is used only by the deploy job.
- One app login holds every module role, as the app is one process. The module roles still make cross-schema access a deliberate grant, and allow separate logins per worker later.
- Reception-board reads (`front_desk.board.read`) and live-update watches (`front_desk.board.watch`) are audited once per user, tenant and exact request (facility) per 5-minute window (`AuditWindow`, MDV-P1-035). The window is per API instance memory, so a restart or a second instance writes one extra row per window; move it into the database before running more than one instance.
- `reporting` has no patients or audit access until MDV-P0-037 (masked views).
