# Database roles, principals, encryption and masking (MDV-P0-008, MDV-P0-009)

Who can do what inside a Medvya database. The roles are defined in one idempotent script, [`infra/sql/roles.sql`](../../infra/sql/roles.sql), which is the single source of truth. Tests: `tests/Medvya.IntegrationTests/DatabaseRoleTests.cs`.

## Roles

| Role | Rights | Never |
|---|---|---|
| `medvya_migrator` | `CREATE TABLE/VIEW/FUNCTION/PROCEDURE/TYPE`, `ALTER ANY SCHEMA`, `REFERENCES`, `ALTER ANY SECURITY POLICY`, `ENABLE LEDGER`, `ALTER ANY MASK`; `SELECT/INSERT/DELETE` on `dbo` (EF migrations history only); `SELECT` on `security` (predicate functions) and `audit` (see Known limits) | Read or write tenancy or patient data, write audit data, create column master/encryption keys, `ALTER DATABASE`, create roles or users, grant rights |
| `medvya_app` | Membership of every module role, nothing else | Any DDL: `CREATE`, `ALTER`, `DROP`, security policies |
| `medvya_tenancy` | `SELECT/INSERT/UPDATE/DELETE ON SCHEMA::tenancy` | Any other schema |
| `medvya_identity` | `SELECT/INSERT/UPDATE ON SCHEMA::identity` (staff accounts) | `DELETE` (offboarding is a membership status), any other schema |
| `medvya_patients` | `SELECT/INSERT/UPDATE/DELETE/UNMASK ON SCHEMA::patients`; `VIEW ANY COLUMN MASTER KEY DEFINITION`, `VIEW ANY COLUMN ENCRYPTION KEY DEFINITION` (key metadata only, for parameter encryption) | Any other schema; decrypting without the column master key |
| `medvya_billing` | `SELECT/INSERT/UPDATE ON SCHEMA::billing` | `DELETE` (deleting a DOC_SEQUENCE row would restart a legal number series), `ALTER` (so it cannot disable the trigger that refuses an `UPDATE` rewinding, skipping or moving a series), any other schema |
| `medvya_scheduling` | `SELECT/INSERT/UPDATE/DELETE ON SCHEMA::scheduling` (templates, resources; appointments from MDV-P1-021) | Any other schema; reporting has no access until a reason-free view exists (appointments hold the SensitiveHealth visit reason) |
| `medvya_audit` | `SELECT/INSERT ON SCHEMA::audit` | `UPDATE`/`DELETE` (the ledger table refuses them too), any other schema |
| `medvya_reporting` | `SELECT ON SCHEMA::tenancy` and `SCHEMA::patients` (masked, identifiers as ciphertext). Support staff use this role | Any write, any DDL, `UNMASK`, `audit` (until `ClientIp` is masked), `identity` |

Rules:

- **Row-level security applies to every role.** A principal without a tenant `SESSION_CONTEXT` sees no clinic rows, reporting included. The RLS predicate functions need no grant for the app: SQL Server evaluates security predicates without checking the querying user's rights (`AppRoleWorksThroughRowLevelSecurity`).
- **Database settings are the administrator's.** `roles.sql` turns on snapshot isolation (needed for ledger verification), so the migrator needs no `ALTER DATABASE`, which would also let it create users and change role membership.
- **Adding a module:** add its schema and `medvya_<module>` role to `roles.sql`, grant only `ON SCHEMA::<module>`, add the role to `medvya_app`, and add a cross-schema test to `DatabaseRoleTests`. Put RLS predicate functions in the `security` schema, so the migrator needs no `SELECT` on the module schema.

## Order of operations

1. The administrator runs `roles.sql` (on every deploy; it is idempotent and also covers new modules).
2. The administrator runs `ColumnEncryption.EnsureKeysAsync` (Always Encrypted key metadata, idempotent; see below).
3. The migration principal runs EF migrations (`dotnet ef database update`, one per module context).
4. The application starts as the app principal.

The integration test fixture does exactly this: `roles.sql` and the keys as `sa`, then every migration impersonating a `medvya_migrator` member. A migration that needs more than the migrator role fails the whole suite.

## Always Encrypted (MDV-P0-009)

National and insurance identifiers are encrypted in the client (SqlClient) with a column encryption key (CEK) that SQL Server stores only wrapped by a column master key (CMK). The CMK never reaches the database, so a DBA or `sa` sees ciphertext (`varbinary`) and cannot decrypt. Protected columns (kept deliberately small, see [data-classification.md](data-classification.md#protected-column-sets)):

| Column | Encryption | Holds |
|---|---|---|
| `patients.Identifiers.LookupValue` | Deterministic, `Latin1_General_BIN2` | ABHA number, ABHA address, Emirates ID (exact-match lookup) |
| `patients.Identifiers.DisplayValue` | Randomized | Aadhaar last 4, passport, visa file number |
| `patients.PayerCoverages.MemberNumber` | Deterministic, `Latin1_General_BIN2` | Insurance member / policy number (exact match for eligibility and claims) |

- **Keys, same shape everywhere.** `ColumnEncryption.EnsureKeysAsync(adminConnection, providerName, keyPath, provider)` (Patients.Infrastructure) creates `CMK_Medvya` (metadata: provider name + key path) and a random 256-bit `CEK_Patients` wrapped by the CMK, unless they exist. Migrations then create the encrypted columns on `CEK_Patients` (raw SQL in `InitialPatients`, as EF Core cannot express `ENCRYPTED WITH`). Only the key store provider differs:
  - **Azure (MDV-P0-006, not built yet):** the Azure Key Vault provider (`Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider`), CMK an RSA key in the country Key Vault; only the API managed identity gets unwrap, the deploy identity gets wrap for key creation.
  - **Developer machines and tests:** `LocalKeyStoreProvider` (`MEDVYA_LOCAL_RSA`), an RSA key held by the process, because the Windows certificate store provider does not exist on macOS/Linux. Tests generate a key per test run; `pnpm dev:db` keeps one at `~/.config/Medvya/local-column-master-key.pem` (macOS/Linux application data folder, owner-only permissions), outside the repository like user secrets. It protects synthetic data only.
- **Connections.** Only the Patients module's connection sets `Column Encryption Setting=Enabled` (`PatientsModule.WithColumnEncryption`), so other modules skip the parameter-encryption round trip. The host registers the key store provider once per process before the first query.
- **Queries.** Deterministic columns support equality only (`=`, joins, grouping); `LIKE`, ranges and sorting on encrypted columns are rejected by SQL Server ("Encryption scheme mismatch"). Lookup input is normalised by the country rules exactly as it was on write, or the ciphertexts would not match. No secure enclave is used.
- Tests: `PatientRegistryTests` (ciphertext for the administrator, exact EID and ABHA lookups through EF Core as `medvya_app`, `LIKE` rejected, protected-column list).

## Dynamic data masking (MDV-P0-009)

`patients.Patients` masks full and Arabic name, date of birth, gender identity, mobile, alternate phone, email (`email()`) and every address column (`default()`). `medvya_patients` holds `UNMASK` on the `patients` schema only (SQL Server 2022 granular UNMASK), so the app reads clear values; `medvya_reporting` (reporting and support) sees `xxxx`, `1900-01-01` and `aXXX@XXXX.com`. Masking limits casual exposure only: it is not a control against a principal that can run arbitrary queries, which is why reporting stays read-only and inside RLS. A later `ALTER COLUMN` on a masked column must restate the mask. Test: `ReportingSeesMaskedDemographicsWhileTheAppSeesClearValues`.

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
- `audit.AuditEvents.ClientIp` is not masked yet (ledger table), so reporting has no `audit` access.
- TDE with a customer-managed key and the Key Vault CMK are Azure work (MDV-P0-006); locally SQL Server uses its default TDE-off setup.
- The deploy pipeline step that runs `EnsureKeysAsync` with the Key Vault provider is not built (MDV-P0-006).
