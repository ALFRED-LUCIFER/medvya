# Environments and the release pipeline

How a release reaches Azure, which GitHub environments and Azure identities that takes, and what the owner sets up by hand. Synthetic data only in dev and test (AGENTS.md 1.1).

## The flow: build once, promote the same build

| Environment | Resource group | Gets a release | GitHub environment | Infra (Bicep) |
|---|---|---|---|---|
| test | `rg-medvya-test-in` (to create) | first, automatically | `test` | `test-infra` |
| dev | `rg-medvya-dev-in` | after test, once a reviewer approves | `dev` | `dev-infra` |
| prod | later (MDV-P1-039) | after dev, with its own reviewer | `prod` (later) | `prod-infra` (later) |

1. Tag a commit on `main` (`git tag v0.0.N <sha> && git push origin v0.0.N`), or run **release** by hand from `main` (`gh workflow run release.yml --ref main`).
2. `release.yml` job **build** (no Azure identity) refuses a commit that is not on `main` or whose `pr` run is not green (it waits up to 25 minutes for a run still going, #1249). It then builds the clinic app, the console and the database migrator once and uploads them as one artifact (`release`, kept 30 days).
3. **deploy-test** runs `deploy-env.yml` with environment `test`. While `test` has no `AZURE_WEBAPP_NAME` variable it skips, green, and dev follows straight away. That is how the pipeline works today, with only dev provisioned.
4. **deploy-dev** waits for test, then for the `dev` reviewer, then deploys the same artifact.
5. Each deploy wakes a stopped database and stopped web apps, uploads the migrator, runs the database job inside the VNet, deploys the clinic app and smoke-tests it, then the console (when `AZURE_CONSOLE_WEBAPP_NAME` is set).

Production is not wired. When it is: a third job in `release.yml` after `deploy-dev`, calling `deploy-env.yml` with `environment: prod`. `deploy-env.yml` already runs `tools/check-legal-marker.sh` for any environment other than dev and test, so it refuses to deploy while the terms are still a draft (MDV-P1-044).

**Why the split.** The build runs `pnpm install` (allow-listed install scripts) and `dotnet publish` (NuGet MSBuild targets): third-party code. It used to run in the same job as the Azure token, whose identity was Owner on the dev resource group. Now no job that runs third-party build code can get an Azure token, and the job that can only downloads an artifact and runs `az`.

## GitHub environments (Settings → Environments)

| Environment | Deployment branches and tags | Required reviewer | Variables |
|---|---|---|---|
| `test` | tags `v*` and branch `main` (manual releases, and `test-power.yml` runs from `main`) | none | the deploy set below |
| `dev` | tags `v*` and branch `main` | the owner | the deploy set below |
| `test-infra`, `dev-infra` | branch `main` only | the owner | the infra set below |
| `prod`, `prod-infra` | later (MDV-P1-039) | the owner, and a second person when there is one | |

- **Deploy set:** `AZURE_CLIENT_ID` (that environment's deploy identity), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `AZURE_WEBAPP_NAME`, `AZURE_STORAGE_ACCOUNT`, `AZURE_DB_JOB`, `AZURE_CONSOLE_WEBAPP_NAME` (the last four are outputs of the Bicep deployment: `appName`, `storageAccount`, `dbJob`, `consoleName`).
- **Infra set:** `AZURE_CLIENT_ID` (that environment's infra identity), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `AZURE_DB_ADMIN_OBJECT_ID`, `STAFF_SIGNIN_CLIENT_ID`, `AZURE_DEPLOY_PRINCIPAL_ID` (object id of that environment's **deploy** identity, which the template lets upload the migrator), `CONSOLE_SIGNIN_CLIENT_ID`, `CONSOLE_OWNER_OBJECT_ID`. Secrets `ALERT_EMAIL` and `AZURE_DB_ADMIN_LOGIN` (repository secrets, or per environment if test uses another administrator). The repository is public, so its logs are: these two stay secrets, and the what-if prints resource ids only.
- GitHub creates an environment the first time a workflow names it, with no rules. `release.yml` and `test-power.yml` name `test`, so set its rules before giving it any variable.

## Two Azure identities per environment

| Identity | Role | Federated credential subject | Used by |
|---|---|---|---|
| deploy (`Medvya CI deploy (<env>)`) | **Contributor** on `rg-medvya-<env>-in` | `repo:ALFRED-LUCIFER/medvya:environment:<env>` | `deploy-env.yml`, `test-power.yml` |
| infra (`Medvya CI infra (<env>)`) | **Owner** on `rg-medvya-<env>-in` (the template creates role and policy assignments) | `repo:ALFRED-LUCIFER/medvya:environment:<env>-infra` | `infra.yml` |

The deploy identity also gets Storage Blob Data Contributor on the `deploy` container from the template (`deployPrincipalId`), which Contributor alone does not give.

**Dev today** has one identity for both (`AZURE_CLIENT_ID` is the same on `dev` and `dev-infra`, Owner on the group). Everything keeps working with that. To split it: create the deploy app registration below, set its client id as `AZURE_CLIENT_ID` on `dev` and its object id as `AZURE_DEPLOY_PRINCIPAL_ID` on `dev-infra`, run **infra** for dev (it grants the new identity the `deploy` container), and only then remove the old identity's `environment:dev` federated credential.

## Owner-only steps for test (Azure and GitHub, in order)

1. `az group create -n rg-medvya-test-in -l centralindia --tags app=medvya env=test data=synthetic-only`
2. Two app registrations with a service principal each, with federated credentials (issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`): deploy, subject `…:environment:test`; infra, subject `…:environment:test-infra`. Role assignments: deploy **Contributor**, infra **Owner**, both on `rg-medvya-test-in` only.
3. GitHub environments `test` and `test-infra` with the rules and variables above (leave `AZURE_WEBAPP_NAME` unset on `test` until step 7).
4. Test staff sign-in: a single-tenant Entra app registration for `https://app-medvya-test-<suffix>.azurewebsites.net/signin-oidc` and `/signout-callback-oidc`, the `amr` optional claim, and MFA for test users (`infra/bicep/README.md`, "Manual Entra settings"). Its client id is `STAFF_SIGNIN_CLIENT_ID` on `test-infra`. The console: `infra/entra/console-app.sh` for the test console host (`docs/runbooks/platform-console-azure.md`).
5. Key Vault secrets the template reads before the first apply: `platform-console-client-secret` (step 4 writes it). The vault is created by the first apply, so run **infra** for test once, let it fail at the console secret if needed, write the secret, and run it again. Then `audit-digest-key` and `oidc-client-secret`, never echoed (`infra/bicep/README.md`, "First deployment").
6. Grant the app's identity **Key Vault Secrets User** on the vault (for its two Key Vault references). It is not in the template on purpose; see the traps below. Restart the PostgreSQL server once (`shared_preload_libraries`).
7. Set `AZURE_WEBAPP_NAME`, `AZURE_STORAGE_ACCOUNT`, `AZURE_DB_JOB`, `AZURE_CONSOLE_WEBAPP_NAME` on `test` from the deployment outputs. The next release deploys test first.

## Cost of test (Central India, list prices, about US$24 to 33 a month)

| Item | Monthly |
|---|---|
| App Service plan B1 (one instance, both web apps; bills even when the apps are stopped) | about $13 |
| PostgreSQL Flexible B1ms, stopped 20:00 to 08:00 IST on weekdays and all weekend by `test-power.yml` (about 60 of 168 hours on) | about $4 to 5 (about $12.50 if never stopped) |
| PostgreSQL storage 32 GB, plus 7-day backups | about $4 |
| Log Analytics (0.5 GB a day cap; first 5 GB a month free), Key Vaults, Storage, Container Apps job (per run), Communication Services | about $3 to 10 |
| Budget alert | 2,500 INR (`test.bicepparam`) |

Nights and weekends the database is stopped: a release then wakes it (deploy step "Start PostgreSQL and the web apps if stopped"). To use test outside hours, run **test-power** with `start`. Azure starts a server that has been stopped for 7 days on its own.

## Traps

- **Keep `rg-medvya-test-in` for good.** Key Vaults have purge protection and the names are deterministic (`kv-medvya-test-<suffix>`, from the group id). Delete the group and recreate it, and the next apply fails on a name still held by the soft-deleted vault for 90 days. Stop it instead (`test-power.yml`).
- **Never lock the ledger digest policy in test** (`ledgerDigestPolicyLocked=false`, 1-day retention): a locked policy makes the storage account undeletable.
- **Never grant by hand what the template grants.** A role assignment made by hand has another name than the template's, so the next apply fails with "The role assignment already exists" (9 Nov in dev). The one exception is the app's Key Vault Secrets User on the vault: dev has it by hand, so putting it in the template would fail dev's next apply. Template it only after removing dev's hand-made assignment.
- **Geo-redundant backup can only be chosen when a server is created.** Dev and test have none (`geoRedundantBackup` defaults to on for prod only). Turning it on later means restoring to a new server with it set and repointing the app, so decide before real data. With the customer-managed key it also needs a key and identity in the paired region (`shortcut:` in `postgres.bicep`).
- **One instance only.** SignalR has no backplane and rate limits are in memory (`shortcut:` on the App Service plan). Never scale out the plan.
- **Private endpoints for Key Vault and Storage** are not in the template: they cost money and the data is synthetic. They come with the first environment that holds real data (MDV-P1-039), with Defender plans and a staging slot.
- Storage refuses account keys (`allowSharedKeyAccess: false`): use `--auth-mode login` with `az storage`, and grant yourself a data role on a container to browse it.
