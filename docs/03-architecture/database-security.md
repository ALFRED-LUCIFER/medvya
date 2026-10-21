# Database roles and principals (MDV-P0-008)

Who can do what inside a Medvya database. The roles are defined in one idempotent script, [`infra/sql/roles.sql`](../../infra/sql/roles.sql), which is the single source of truth. Tests: `tests/Medvya.IntegrationTests/DatabaseRoleTests.cs`.

## Roles

| Role | Rights | Never |
|---|---|---|
| `medvya_migrator` | `CREATE TABLE/VIEW/FUNCTION/PROCEDURE/TYPE`, `ALTER ANY SCHEMA`, `REFERENCES`, `ALTER ANY SECURITY POLICY`, `ENABLE LEDGER`; `SELECT/INSERT/DELETE` on `dbo` (EF migrations history only); `SELECT` on `security` (predicate functions) and `audit` (see Known limits) | Read or write tenancy data, write audit data, `ALTER DATABASE`, create roles or users, grant rights |
| `medvya_app` | Membership of every module role, nothing else | Any DDL: `CREATE`, `ALTER`, `DROP`, security policies |
| `medvya_tenancy` | `SELECT/INSERT/UPDATE/DELETE ON SCHEMA::tenancy` | Any other schema |
| `medvya_audit` | `SELECT/INSERT ON SCHEMA::audit` | `UPDATE`/`DELETE` (the ledger table refuses them too), any other schema |
| `medvya_reporting` | `SELECT ON SCHEMA::tenancy` | Any write, any DDL, `audit` (until masking exists) |

Rules:

- **Row-level security applies to every role.** A principal without a tenant `SESSION_CONTEXT` sees no clinic rows, reporting included. The RLS predicate functions need no grant for the app: SQL Server evaluates security predicates without checking the querying user's rights (`AppRoleWorksThroughRowLevelSecurity`).
- **Database settings are the administrator's.** `roles.sql` turns on snapshot isolation (needed for ledger verification), so the migrator needs no `ALTER DATABASE`, which would also let it create users and change role membership.
- **Adding a module:** add its schema and `medvya_<module>` role to `roles.sql`, grant only `ON SCHEMA::<module>`, add the role to `medvya_app`, and add a cross-schema test to `DatabaseRoleTests`. Put RLS predicate functions in the `security` schema, so the migrator needs no `SELECT` on the module schema.

## Order of operations

1. The administrator runs `roles.sql` (on every deploy; it is idempotent and also covers new modules).
2. The migration principal runs EF migrations (`dotnet ef database update`, one per module context).
3. The application starts as the app principal.

The integration test fixture does exactly this: `roles.sql` as `sa`, then every migration impersonating a `medvya_migrator` member. A migration that needs more than the migrator role fails the whole suite.

## Mapping to Azure (MDV-P0-006)

Azure provisioning is MDV-P0-006; until it exists, these steps are the plan, not code.

| Principal | Azure identity | Role membership |
|---|---|---|
| Administrator | Entra group set as the logical server's Entra admin; members gain it only through just-in-time elevation (PIM, runbook MDV-P0-025) | `db_owner` via Entra admin; runs `roles.sql` |
| Migration | User-assigned managed identity used only by the GitHub Actions deploy job (OIDC federated credential) | `medvya_migrator` |
| Application | App Service managed identity (one per country deployment) | `medvya_app` |
| Reporting | Entra group for reporting users or a reporting service identity | `medvya_reporting` |

Mapping, run by the administrator after `roles.sql`, per database:

```sql
CREATE USER [<app-service-identity-name>] FROM EXTERNAL PROVIDER;
ALTER ROLE medvya_app ADD MEMBER [<app-service-identity-name>];
CREATE USER [<migration-identity-name>] FROM EXTERNAL PROVIDER;
ALTER ROLE medvya_migrator ADD MEMBER [<migration-identity-name>];
```

Also in MDV-P0-006, not testable locally: Entra-only authentication on the logical server (no SQL logins or passwords exist), and connection strings of the form `Server=tcp:<server>.database.windows.net;Database=<db>;Authentication=Active Directory Managed Identity;Encrypt=Strict` with no password. Locally, tests stand in for each identity with a database user `WITHOUT LOGIN` (no password) impersonated through `EXECUTE AS USER`.

## Known limits

- The audit module's RLS predicate lives in the `audit` schema (migration `InitialAudit`), so the migrator has `SELECT` on `audit`. RLS still hides rows from it. Moving the predicate to `security` lets that grant go.
- One app principal holds every module role, as the app is one process. The module roles still make cross-schema access a deliberate grant, and they allow separate principals per worker later (for example a jobs worker with only the roles it needs).
- Dynamic data masking for reporting and support roles comes with the Patients module (see [data-classification.md](data-classification.md)).
