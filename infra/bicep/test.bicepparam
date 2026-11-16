// India test (rg-medvya-test-in): the pilot flow on every release before dev (docs/runbooks/environments.md). Synthetic data only.
// Applied by .github/workflows/infra.yml from the GitHub environment test-infra.
using 'main.bicep'

param env = 'test'
param dbAdminObjectId = readEnvironmentVariable('DB_ADMIN_OBJECT_ID')
param dbAdminLogin = readEnvironmentVariable('DB_ADMIN_LOGIN')
param alertEmail = readEnvironmentVariable('ALERT_EMAIL')
param oidcClientId = readEnvironmentVariable('OIDC_CLIENT_ID')
param deployPrincipalId = readEnvironmentVariable('DEPLOY_PRINCIPAL_ID')
param consoleClientId = readEnvironmentVariable('CONSOLE_CLIENT_ID')
param consoleOwnerObjectId = readEnvironmentVariable('CONSOLE_OWNER_OBJECT_ID')
// The pilot journeys set up a 3-doctor clinic with 5 other staff, as in dev.
param planDoctors = 3
param planStaff = 5
param budgetAmount = 2500 // INR a month; the expected bill is about US$24-33 (runbook)
// One day, and never locked: a locked policy keeps the storage account (and so the resource group) undeletable while digests are retained.
param ledgerDigestRetentionDays = 1
param ledgerDigestPolicyLocked = false
