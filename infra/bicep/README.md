# infra/bicep: India dev environment (MDV-P0-006)

`main.bicep` is the cheapest viable dev environment in Central India. It is for synthetic data only; the `shortcut:` comment at the top lists what is deliberately missing.

```
az group create -n rg-medvya-dev-in -l centralindia
az deployment group what-if -g rg-medvya-dev-in -f infra/bicep/main.bicep -p dbAdminObjectId=<entra object id> dbAdminLogin=<upn> alertEmail=<email> oidcClientId=<app registration client id> deployPrincipalId=<object id of the deploy service principal>
az deployment group create  -g rg-medvya-dev-in -f infra/bicep/main.bicep -p <same parameters>
```

- Sign-in is a single-tenant Entra app registration. Its client secret lives only in Key Vault (`oidc-client-secret`); the web app reads it through a Key Vault reference.
- Cost guard: a monthly budget (default 1,800 INR) mails alerts at 50/80/100 %; Log Analytics is capped at 0.1 GB/day.
- No secret value belongs in this folder.

## Database (MDV-P0-038)

`postgres.bicep` is one Azure Database for PostgreSQL Flexible Server per country deployment. It replaces the Azure SQL server, database and firewall-rule module (the `restore-me` and `admin-me` SQL firewall rules were manual and already deleted; no firewall rule exists in the template any more).

- **Private only:** the server sits in a delegated subnet of `vnet-...` with a private DNS zone; public network access is off. The web app reaches it through VNet integration, so the App Service plan must be B1 or higher (the default is now B1).
- **Entra-only:** password authentication is off. `dbAdminObjectId` / `dbAdminLogin` is the only administrator; the app's managed identity is mapped to a least-privilege role by that administrator, never made an administrator.
- **Customer-managed key:** key `medvya-postgres-storage` in the vault; a user-assigned identity holds Crypto Service Encryption User on that key only.
- **In country:** the module takes `country` (`IN` or `AE`) and fails the deployment if the region, or the geo-backup pair when `geoRedundantBackup` is on, is outside it. The Azure Policy assignment `allowed-locations-in` already evaluates PostgreSQL servers (it checks the location of every resource).
- **Production defaults** (`env=prod`): zone-redundant HA, General Purpose SKU, 14-day point-in-time restore. Dev: Burstable, no HA, 7 days.
- **Server parameters:** TLS 1.2+, `log_connections`, `log_disconnections`, pgaudit (`read, write, ddl, role`), extensions allow-list `pgcrypto`, `pg_stat_statements`, `pgaudit`. After the first deployment restart the server once so `shared_preload_libraries` takes effect.
- Restore drill: `docs/runbooks/postgres-restore-drill.md`.

## App settings and the deploy job (MDV-P0-042)

The app reads exactly what the template sets: `ConnectionStrings__Medvya` (host, database, the app's name as the user, `SslMode=VerifyFull`, **no password**: the app signs in with an Entra token for its managed identity), `NationalIdProtection__KeyVaultKeyId` (the **versionless** id of key `medvya-national-id`, so a new key version needs no deployment), `Audit__DigestKey` (Key Vault reference to secret `audit-digest-key`), `Storage__BlobEndpoint` (ledger digests with the managed identity) and `Jobs__Enabled`.

The database is private, so a laptop or a GitHub runner cannot reach it. `job-db-<env>-<suffix>` (Container Apps job in `snet-jobs`) does every database step of a deploy. The workflow uploads a self-contained `db-migrate` to container `deploy` and starts the job. The job then runs `roles.sql` and maps the app and migrator logins as `id-dbadmin-*` (a second Entra administrator, used only by this job), and migrates as `id-migrate-*` (a login `IN ROLE medvya_migrator`). The workflow ships the app only after the job succeeds. Its console log is in Log Analytics (`ContainerAppConsoleLogs_CL`). See `docs/03-architecture/database-security.md`.

**Ledger digests:** container `ledger-digests` has version-level immutability with a time-based retention policy (`ledgerDigestRetentionDays`: 7 in dev, 3650 in prod). Nobody can overwrite or delete a digest inside that window. The app holds the custom role "Medvya ledger digest writer" on that container: read and add, no delete. Bicep creates the policy **unlocked**, because locking is an action, not a property. Lock it once per environment, after the first deployment. Locking is **irreversible**: the period can then only grow, and the storage account cannot be deleted while it holds digests under retention.

```
etag=$(az storage container immutability-policy show --account-name <storage> -c ledger-digests --query etag -o tsv)
az storage container immutability-policy lock --account-name <storage> -c ledger-digests --if-match "$etag"
```

**First deployment of an environment, once** (values never echoed or committed):

1. `openssl rand -base64 32 | tr -d '\n' > /tmp/k && az keyvault secret set --vault-name <kv> --name audit-digest-key --file /tmp/k -o none && rm /tmp/k`
2. The app's identity needs **Key Vault Secrets User** on the vault for the two Key Vault references (`oidc-client-secret`, `audit-digest-key`). In dev it was granted by hand; it is not in the template because a second assignment with the same role and scope would fail the deployment.
3. Deploy the template, then restart the PostgreSQL server once (`shared_preload_libraries`).
4. Set the `dev` environment variables `AZURE_STORAGE_ACCOUNT` and `AZURE_DB_JOB` (outputs `storageAccount`, `dbJob`) and delete `AZURE_SQL_SERVER`. Then run `deploy-dev`.

## Web app (MDV-P0-007)

The API serves the built clinic app from `wwwroot/app` under `/app/` (the public site keeps `/`), so the strict session cookie needs no CORS. The deploy workflow builds `web/clinic` (Vite base `/app/`) and copies `dist` into the publish folder. Settings that point server-made links there: `Web__AppPath=/app/`, `Invitations__LinkBaseUrl`, `PublicSite__Deployments__SignUpUrls__IN`.

## Manual Entra settings (not in Bicep)

Staff sign-in needs both, or the app refuses every sign-in with "the provider did not report multi-factor authentication" (MDV-P0-011):

1. **MFA enforced for the user.** Free tier: Security defaults off, then Per-user MFA = Enforced (Conditional Access needs a paid licence). Production moves staff sign-in to an Entra External ID tenant.
2. **`amr` optional claim on the app registration** (Token configuration → Add optional claim → ID → `amr`). v2.0 ID tokens omit it otherwise. CLI: `az ad app update --id <client id> --optional-claims '{"idToken":[{"name":"amr","essential":false,"additionalProperties":[]}]}'`.

## Email (MDV-P1-032)

Invitations (add doctor, add staff, onboarding) need a working sender; the app refuses to send without one. Two providers, chosen by the `emailProvider` parameter (see `docs/03-architecture/email-providers.md`):

- **`Acs` (default): Azure Communication Services Email.** Created by this template (Azure-managed sender domain, data location `emailDataLocation`, default India). The app sends with its managed identity: no key or password. Register the provider once: `az provider register -n Microsoft.Communication`.
- **`Smtp`: SendGrid's relay.** Verify a sender in SendGrid (a domain you own is best), create an API key with only "Mail Send", store it without echoing: `az keyvault secret set --vault-name <kv> --name sendgrid-api-key --value "<key>"`, then deploy with `emailProvider=Smtp emailFrom=<verified sender>`.
