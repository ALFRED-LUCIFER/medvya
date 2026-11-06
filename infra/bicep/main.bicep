// MDV-P0-006: India dev environment, cheapest viable. Resource-group scope.
// MDV-P0-038: the database is PostgreSQL Flexible Server, private (VNet-integrated), Entra-only login, customer-managed key.
// shortcut: dev only. No Defender plans, no staging slot, Storage keeps a public endpoint (private endpoints cost money).
// Add them before any real patient data or a staging/prod environment.
targetScope = 'resourceGroup'

param location string = 'centralindia'
param env string = 'dev'
@description('Entra object id of the user or group that administers PostgreSQL (Entra-only auth, no password administrator).')
param dbAdminObjectId string
@description('Sign-in name (UPN or group name) of that administrator.')
param dbAdminLogin string
@allowed(['User', 'Group', 'ServicePrincipal'])
param dbAdminType string = 'User'
param alertEmail string
@description('Country of this deployment. Backups and restore stay inside it; the Postgres module rejects a region or backup pair outside it.')
@allowed(['IN', 'AE'])
param country string = 'IN'
param databaseName string = 'medvya'
@description('Zone-redundant HA (needs a General Purpose or Memory Optimized SKU). On for production.')
param postgresHaEnabled bool = env == 'prod'
@description('Point-in-time restore window in days (7 to 35). Production 14; dev 7.')
@minValue(7)
@maxValue(35)
param pitrRetentionDays int = env == 'prod' ? 14 : 7
@description('Copy backups to the Azure paired region. Only valid when the pair is in the same country; the module checks.')
param geoRedundantBackup bool = false
param postgresSkuName string = env == 'prod' ? 'Standard_D2ds_v5' : 'Standard_B1ms'
@allowed(['Burstable', 'GeneralPurpose', 'MemoryOptimized'])
param postgresSkuTier string = env == 'prod' ? 'GeneralPurpose' : 'Burstable'
@description('Which sender the app uses: Acs (Azure Communication Services, default) or Smtp (SendGrid relay).')
@allowed(['Acs', 'Smtp'])
param emailProvider string = 'Acs'
@description('Smtp only: sender address verified in SendGrid.')
param emailFrom string = ''
@description('Where Azure Communication Services keeps email data (data residency). Must be a location the service offers.')
param emailDataLocation string = 'India'
@description('Application (client) id of the Entra app registration the staff sign-in uses. Not a secret.')
param oidcClientId string
@description('Monthly budget in the billing currency (INR).')
param budgetAmount int = 1800
param budgetStart string = utcNow('yyyy-MM-01')
@description('B1 or higher: VNet integration (the app reaching the private database) is not available on F1.')
param appServiceSku string = 'B1'

var suffix = take(uniqueString(resourceGroup().id), 6)
var name = 'medvya-${env}-${suffix}'
// The app's own address; a site cannot reference its own property inside its own settings.
var appHost = 'app-${name}.azurewebsites.net'
var tags = { app: 'medvya', env: env, country: country, data: 'synthetic-only' }

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

// A deleted or overwritten image can be recovered for a week (doctor signatures and logos live here).
resource blobProtection 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 7 }
    containerDeleteRetentionPolicy: { enabled: true, days: 7 }
    isVersioningEnabled: true
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

// Always Encrypted column master key (MDV-P0-009).
// shortcut: kept until application-level encryption of national IDs replaces it (ADR-012, MDV-P0-035); remove it then. RSA 3072, wrap/unwrap/sign/verify only.
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
  name: guid(cmk.id, dbAdminObjectId, cryptoUser)
  properties: { roleDefinitionId: cryptoUser, principalId: dbAdminObjectId, principalType: 'User' }
}

// Transactional email (MDV-P1-032): Azure Communication Services Email with an Azure-managed sender domain (no domain to own or verify).
// The app sends with its managed identity (role below): no key or password exists. Needs the Microsoft.Communication provider registered.
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'email-${name}'
  location: 'global' // the service is global; dataLocation is where its data lives
  tags: tags
  properties: { dataLocation: emailDataLocation }
}

resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: { domainManagement: 'AzureManaged' }
}

resource comms 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-${name}'
  location: 'global'
  tags: tags
  properties: { dataLocation: emailDataLocation, linkedDomains: [emailDomain.id] }
}

// "Communication and Email Service Owner" on this one resource lets the app (and nothing else) send through it.
var emailOwner = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '09976791-48a7-449e-bb21-39d1a415f350')
resource commsApp 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: comms
  name: guid(comms.id, app.id, emailOwner)
  properties: { roleDefinitionId: emailOwner, principalId: app.identity.principalId, principalType: 'ServicePrincipal' }
}

// Network for the private database (MDV-P0-038): the server sits in a delegated subnet; the app reaches it through VNet integration.
// The private DNS zone resolves the server name to its private address inside the VNet only.
resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-${name}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.20.0.0/16'] }
    subnets: [
      {
        name: 'snet-postgres'
        properties: {
          addressPrefix: '10.20.1.0/24'
          delegations: [{ name: 'postgres', properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' } }]
        }
      }
      {
        name: 'snet-app'
        properties: {
          addressPrefix: '10.20.2.0/24'
          delegations: [{ name: 'app', properties: { serviceName: 'Microsoft.Web/serverFarms' } }]
        }
      }
    ]
  }
}

resource pgDns 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: '${name}.private.postgres.database.azure.com'
  location: 'global'
  tags: tags
}

resource pgDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: pgDns
  name: 'vnet'
  location: 'global'
  properties: { virtualNetwork: { id: vnet.id }, registrationEnabled: false }
}

// Storage encryption key (customer-managed). The server's identity may only wrap and unwrap with this one key.
resource pgKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'medvya-postgres-storage'
  properties: {
    kty: 'RSA'
    keySize: 3072
    keyOps: ['wrapKey', 'unwrapKey']
  }
}

resource pgIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-psql-${name}'
  location: location
  tags: tags
}

// "Key Vault Crypto Service Encryption User" on the key only.
var cryptoServiceUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'e147488a-f6f5-4113-8e2d-b22465e65bf6')
resource pgKeyAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: pgKey
  name: guid(pgKey.id, pgIdentity.id, cryptoServiceUser)
  properties: { roleDefinitionId: cryptoServiceUser, principalId: pgIdentity.properties.principalId, principalType: 'ServicePrincipal' }
}

module postgres 'postgres.bicep' = {
  name: 'postgres'
  dependsOn: [pgKeyAccess, pgDnsLink]
  params: {
    name: name
    location: location
    tags: tags
    country: country
    databaseName: databaseName
    delegatedSubnetId: vnet.properties.subnets[0].id
    privateDnsZoneId: pgDns.id
    entraAdminObjectId: dbAdminObjectId
    entraAdminLogin: dbAdminLogin
    entraAdminType: dbAdminType
    cmkIdentityId: pgIdentity.id
    cmkKeyUri: pgKey.properties.keyUri
    haEnabled: postgresHaEnabled
    pitrRetentionDays: pitrRetentionDays
    geoRedundantBackup: geoRedundantBackup
    skuName: postgresSkuName
    skuTier: postgresSkuTier
    logAnalyticsId: logs.id
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

// Email app settings by provider (docs/03-architecture/email-providers.md). Without a working provider the mailer fails closed and
// invitations (add doctor, add staff) are refused (MDV-P1-032).
var emailSettings = emailProvider == 'Acs' ? [
  { name: 'Email__Provider', value: 'Acs' }
  { name: 'Email__Acs__Endpoint', value: 'https://${comms.properties.hostName}' }
  { name: 'Email__Acs__From', value: 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}' }
] : [
  // SendGrid's SMTP relay (smtp.sendgrid.net, user apikey, in appsettings.json); the API key is a Key Vault reference.
  { name: 'Email__Provider', value: 'Smtp' }
  { name: 'Email__Smtp__From', value: emailFrom }
  { name: 'Email__Smtp__Password', value: '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=sendgrid-api-key)' }
]

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${name}'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    virtualNetworkSubnetId: vnet.properties.subnets[1].id // outbound traffic to the private database
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: concat(emailSettings, [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Staging' }
        { name: 'Onboarding__DeploymentCountry', value: 'IN' }
        { name: 'Storage__AccountName', value: storage.name }
        // The API serves the built web app under /app/ (MDV-P0-007); links the server creates must point there.
        { name: 'Web__AppPath', value: '/app/' }
        { name: 'Invitations__LinkBaseUrl', value: 'https://${appHost}/app' }
        { name: 'PublicSite__Deployments__SignUpUrls__IN', value: 'https://${appHost}/app/signup' }
        // No password anywhere: the app signs in with an Entra token for its managed identity (mapped to the app role by the administrator).
        // shortcut: host and database only; the app code (MDV-P0-033/034) decides the final setting names. Align when it lands.
        { name: 'Postgres__Host', value: postgres.outputs.fqdn }
        { name: 'Postgres__Database', value: postgres.outputs.databaseName }
        // Staff sign-in (MDV-P0-011): single-tenant Entra app; the client secret is a Key Vault reference, never a value here.
        { name: 'Authentication__Oidc__Authority', value: '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0' }
        { name: 'Authentication__Oidc__ClientId', value: oidcClientId }
        { name: 'Authentication__Oidc__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=oidc-client-secret)' }
        { name: 'ColumnEncryption__KeyVaultKeyUrl', value: cmk.properties.keyUriWithVersion }
      ])
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

// Built-in "Allowed locations": data stays in India (AGENTS.md 1.6). It evaluates every resource type that has a location,
// including Microsoft.DBforPostgreSQL/flexibleServers, so the database is covered by this one assignment (MDV-P0-038).
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
output postgresServer string = postgres.outputs.serverName
output postgresHost string = postgres.outputs.fqdn
output appPrincipalId string = app.identity.principalId
output keyVault string = vault.name
