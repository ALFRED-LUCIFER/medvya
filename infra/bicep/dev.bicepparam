// India dev (rg-medvya-dev-in). Applied by .github/workflows/infra.yml; the ids come from the GitHub environment dev-infra, the alert
// email and the administrator's sign-in name from secrets (the repository and its logs are public).
using 'main.bicep'

param env = 'dev'
param dbAdminObjectId = readEnvironmentVariable('DB_ADMIN_OBJECT_ID')
param dbAdminLogin = readEnvironmentVariable('DB_ADMIN_LOGIN')
param alertEmail = readEnvironmentVariable('ALERT_EMAIL')
param oidcClientId = readEnvironmentVariable('OIDC_CLIENT_ID')
param deployPrincipalId = readEnvironmentVariable('DEPLOY_PRINCIPAL_ID')
param consoleClientId = readEnvironmentVariable('CONSOLE_CLIENT_ID')
param consoleOwnerObjectId = readEnvironmentVariable('CONSOLE_OWNER_OBJECT_ID')
param ledgerDigestPolicyLocked = true // locked 13 Nov 2026 (README, Ledger digests); Azure refuses any write to a locked policy
