// MDV-P0-006: India dev environment, cheapest viable. Resource-group scope.
// shortcut: dev only. No private endpoints, no Defender plans, no staging slot, no VNet integration (all cost money).
// SQL and Storage are reachable over public endpoints, SQL limited to Azure services + Entra-only auth.
// Add them before any real patient data or a staging/prod environment.
targetScope = 'resourceGroup'

param location string = 'centralindia'
param env string = 'dev'
@description('Entra object id of the user or group that administers Azure SQL (Entra-only auth).')
param sqlAdminObjectId string
param sqlAdminLogin string
param alertEmail string
@description('Monthly budget in the billing currency (INR).')
param budgetAmount int = 1800
param budgetStart string = utcNow('yyyy-MM-01')
@description('F1 is free (no always-on); use B1 for a pilot.')
param appServiceSku string = 'F1'

var suffix = take(uniqueString(resourceGroup().id), 6)
var name = 'medvya-${env}-${suffix}'
var tags = { app: 'medvya', env: env, country: 'IN', data: 'synthetic-only' }

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${name}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.1') }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${name}'
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('st${replace(name, '-', '')}', 24)
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${name}'
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
  }
}

// Always Encrypted column master key (MDV-P0-009). Stays in Key Vault: SQL Server stores only its path. RSA 3072, wrap/unwrap/sign/verify only.
resource cmk 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'medvya-column-master'
  properties: {
    kty: 'RSA'
    keySize: 3072
    keyOps: ['wrapKey', 'unwrapKey', 'sign', 'verify']
  }
}

// "Key Vault Crypto User" on this one key, never on the vault: the app reads encrypted columns; the administrator creates the column encryption key.
var cryptoUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')
resource cmkApp 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: cmk
  name: guid(cmk.id, app.id, cryptoUser)
  properties: { roleDefinitionId: cryptoUser, principalId: app.identity.principalId, principalType: 'ServicePrincipal' }
}
resource cmkAdmin 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: cmk
  name: guid(cmk.id, sqlAdminObjectId, cryptoUser)
  properties: { roleDefinitionId: cryptoUser, principalId: sqlAdminObjectId, principalType: 'User' }
}

resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${name}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: tenant().tenantId
      principalType: 'User'
    }
  }
}

// Only this web app's outbound addresses may reach SQL (no "allow Azure services" rule, which admits every Azure customer).
// Authentication is still Entra-only and Encrypt=Strict. possibleOutboundIpAddresses is the stable superset, so it survives scale-out.
// Deploys and the administrator open their own address for the length of the work (deploy-dev.yml; tools/db-migrate runbook).
// shortcut: IP rules on a shared App Service plan; a private endpoint with VNet integration (needs B1 or higher) replaces them before real patient data.
module sqlApp 'sql-firewall.bicep' = {
  name: 'sql-firewall-app'
  params: { serverName: sql.name, addresses: split(app.properties.possibleOutboundIpAddresses, ',') }
}

// Free offer: 100k vCore-seconds/month, auto-pauses when used up (no overage charge).
resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'medvya'
  location: location
  tags: tags
  sku: { name: 'GP_S_Gen5_1', tier: 'GeneralPurpose' }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'asp-${name}'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: appServiceSku }
  properties: { reserved: true }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${name}'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Staging' }
        { name: 'Onboarding__DeploymentCountry', value: 'IN' }
        { name: 'Storage__AccountName', value: storage.name }
        // No password anywhere: the app's managed identity signs in (mapped to medvya_app by the administrator).
        { name: 'ConnectionStrings__Medvya', value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${db.name};Authentication=Active Directory Managed Identity;Encrypt=Strict;' }
        { name: 'ColumnEncryption__KeyVaultKeyUrl', value: cmk.properties.keyUriWithVersion }
      ]
    }
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'budget-${name}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStart }
    notifications: {
      at50: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 50, contactEmails: [alertEmail], thresholdType: 'Actual' }
      at80: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 80, contactEmails: [alertEmail], thresholdType: 'Actual' }
      at100: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 100, contactEmails: [alertEmail], thresholdType: 'Actual' }
    }
  }
}

// Built-in "Allowed locations": data stays in India (AGENTS.md 1.6).
resource region 'Microsoft.Authorization/policyAssignments@2024-04-01' = {
  name: 'allowed-locations-in'
  properties: {
    displayName: 'Medvya India: allowed locations'
    policyDefinitionId: tenantResourceId('Microsoft.Authorization/policyDefinitions', 'e56962a6-4747-49cd-b67b-bf8b01975c4c')
    parameters: { listOfAllowedLocations: { value: ['centralindia', 'southindia'] } }
  }
}

output appName string = app.name
output appHost string = app.properties.defaultHostName
output sqlServer string = sql.properties.fullyQualifiedDomainName
output appPrincipalId string = app.identity.principalId
output keyVault string = vault.name
