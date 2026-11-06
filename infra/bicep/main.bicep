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
@description('Object id of the deploy workflow\'s service principal (GitHub OIDC). It uploads db-migrate for the database job.')
param deployPrincipalId string
@description('Application (client) id of the Entra app registration the staff sign-in uses. Not a secret.')
param oidcClientId string
@description('Monthly budget in the billing currency (INR).')
param budgetAmount int = 3500
param budgetStart string = utcNow('yyyy-MM-01')
@description('B1 or higher: VNet integration (the app reaching the private database) is not available on F1.')
param appServiceSku string = 'B1'
@description('Days a ledger digest cannot be changed or deleted (time-based immutability on ledger-digests). Once the policy is locked it can only grow.')
param ledgerDigestRetentionDays int = env == 'prod' ? 3650 : 7
@description('Image of the database deploy job (migrations and roles inside the VNet). Pinned; the job downloads db-migrate from Blob.')
param dbJobImage string = 'mcr.microsoft.com/azure-cli:2.92.0'

var suffix = take(uniqueString(resourceGroup().id), 6)
var name = 'medvya-${env}-${suffix}'
// The app's own address; a site cannot reference its own property inside its own settings.
var appName = 'app-${name}'
var appHost = '${appName}.azurewebsites.net'
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

// Signed daily digests of the ledger chains (MDV-P0-036/042). Version-level WORM (the account keeps blob versions): a digest cannot be
// overwritten or deleted for ledgerDigestRetentionDays, by anyone. The policy is created unlocked; lock it once per environment
// (README, irreversible), after which not even an account owner can shorten or remove it.
resource digests 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobProtection
  name: 'ledger-digests'
  properties: { publicAccess: 'None', immutableStorageWithVersioning: { enabled: true } }
}

resource digestsRetention 'Microsoft.Storage/storageAccounts/blobServices/containers/immutabilityPolicies@2023-05-01' = {
  parent: digests
  name: 'default'
  properties: { immutabilityPeriodSinceCreationInDays: ledgerDigestRetentionDays, allowProtectedAppendWrites: false }
}

// Append only for the app: read and add blobs, no delete and no container or policy change (the WORM policy refuses an overwrite).
resource digestWriter 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(resourceGroup().id, 'medvya-ledger-digest-writer')
  properties: {
    roleName: 'Medvya ledger digest writer (${name})'
    description: 'Write new ledger digests and read them. No delete.'
    assignableScopes: [resourceGroup().id]
    permissions: [{
      actions: []
      dataActions: [
        'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read'
        'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write'
        'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action'
      ]
    }]
  }
}

resource digestsApp 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: digests
  name: guid(digests.id, app.id, digestWriter.id)
  properties: { roleDefinitionId: digestWriter.id, principalId: app.identity.principalId, principalType: 'ServicePrincipal' }
}

// The deploy workflow uploads the db-migrate build here; the database job downloads and runs it.
resource deployArtifacts 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobProtection
  name: 'deploy'
  properties: { publicAccess: 'None' }
}

var blobReader = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1')
resource deployArtifactsJob 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: deployArtifacts
  name: guid(deployArtifacts.id, dbMigrator.id, blobReader)
  properties: { roleDefinitionId: blobReader, principalId: dbMigrator.properties.principalId, principalType: 'ServicePrincipal' }
}

var blobContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
resource deployArtifactsUpload 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: deployArtifacts
  name: guid(deployArtifacts.id, deployPrincipalId, blobContributor)
  properties: { roleDefinitionId: blobContributor, principalId: deployPrincipalId, principalType: 'ServicePrincipal' }
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

// National-ID envelope encryption (MDV-P0-035): this key wraps each tenant's data keys. RSA 3072, wrap and unwrap only.
// The app gets the versionless id, so a new key version rotates in without a deployment (each data key names the version that wrapped it).
// The Always Encrypted key medvya-column-master (MDV-P0-009) is no longer used; it stays in the vault until it is deleted by hand.
resource nationalIdKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'medvya-national-id'
  properties: {
    kty: 'RSA'
    keySize: 3072
    keyOps: ['wrapKey', 'unwrapKey']
  }
}

// "Key Vault Crypto User" on this one key, never on the vault.
var cryptoUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')
resource nationalIdKeyApp 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: nationalIdKey
  name: guid(nationalIdKey.id, app.id, cryptoUser)
  properties: { roleDefinitionId: cryptoUser, principalId: app.identity.principalId, principalType: 'ServicePrincipal' }
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
      {
        // The database deploy job (Container Apps, workload-profiles environment: /27 minimum).
        name: 'snet-jobs'
        properties: {
          addressPrefix: '10.20.4.0/27'
          delegations: [{ name: 'jobs', properties: { serviceName: 'Microsoft.App/environments' } }]
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
    jobAdminObjectId: dbAdmin.properties.principalId
    jobAdminName: dbAdmin.name
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
  name: appName
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
        // Ledger digests go to Blob with the managed identity (no account key, no connection string).
        { name: 'Storage__BlobEndpoint', value: storage.properties.primaryEndpoints.blob }
        { name: 'Audit__DigestKey', value: '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=audit-digest-key)' }
        // Hangfire runs the reminders and the daily ledger digest and verification.
        { name: 'Jobs__Enabled', value: 'true' }
        // The API serves the built web app under /app/ (MDV-P0-007); links the server creates must point there.
        { name: 'Web__AppPath', value: '/app/' }
        { name: 'Invitations__LinkBaseUrl', value: 'https://${appHost}/app' }
        { name: 'PublicSite__Deployments__SignUpUrls__IN', value: 'https://${appHost}/app/signup' }
        // No password anywhere: the app signs in with an Entra token for its managed identity, a login IN ROLE medvya_app made by the
        // database job (PostgresDataSources). The login name is the app's name. The server certificate is checked.
        { name: 'ConnectionStrings__Medvya', value: 'Host=${postgres.outputs.fqdn};Database=${postgres.outputs.databaseName};Username=${appName};SslMode=VerifyFull' }
        { name: 'NationalIdProtection__KeyVaultKeyId', value: nationalIdKey.properties.keyUri }
        // Staff sign-in (MDV-P0-011): single-tenant Entra app; the client secret is a Key Vault reference, never a value here.
        { name: 'Authentication__Oidc__Authority', value: '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0' }
        { name: 'Authentication__Oidc__ClientId', value: oidcClientId }
        { name: 'Authentication__Oidc__ClientSecret', value: '@Microsoft.KeyVault(VaultName=${vault.name};SecretName=oidc-client-secret)' }
      ])
    }
  }
}

// Database deploy job (MDV-P0-042). The server is private, so migrations run inside the VNet, never from a GitHub runner. Two identities:
// dbAdmin is a second Entra administrator of the server (roles.sql, and the app and migrator logins); dbMigrator is a login IN ROLE
// medvya_migrator that runs the migrations. The deploy workflow uploads db-migrate to the deploy container and starts the job.
resource dbAdmin 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-dbadmin-${name}'
  location: location
  tags: tags
}

resource dbMigrator 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-migrate-${name}'
  location: location
  tags: tags
}

resource jobsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${name}'
  location: location
  tags: tags
  properties: {
    vnetConfiguration: { infrastructureSubnetId: vnet.properties.subnets[2].id, internal: true }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: { customerId: logs.properties.customerId, sharedKey: logs.listKeys().primarySharedKey }
    }
  }
}

// Each step signs in as its own identity (AZURE_CLIENT_ID picks it). Ids and names only; no secret exists.
var dbJobScript = '''
set -euo pipefail
az login --identity --client-id "$MIGRATOR_CLIENT_ID" --allow-no-subscriptions -o none
az storage blob download --auth-mode login --account-name "$STORAGE" -c deploy -n db-migrate.tar.gz -f /tmp/db-migrate.tar.gz -o none
mkdir -p /tmp/db-migrate && tar -xzf /tmp/db-migrate.tar.gz -C /tmp/db-migrate
cd /tmp/db-migrate
db="Host=$PGHOST;SslMode=VerifyFull"
AZURE_CLIENT_ID="$ADMIN_CLIENT_ID" ./db-migrate admin "$db;Database=$PGDATABASE;Username=$ADMIN_NAME"
AZURE_CLIENT_ID="$ADMIN_CLIENT_ID" ./db-migrate entra "$db;Database=postgres;Username=$ADMIN_NAME" medvya_migrator "$MIGRATOR_NAME" "$MIGRATOR_OBJECT_ID"
AZURE_CLIENT_ID="$ADMIN_CLIENT_ID" ./db-migrate entra "$db;Database=postgres;Username=$ADMIN_NAME" medvya_app "$APP_NAME" "$APP_OBJECT_ID"
AZURE_CLIENT_ID="$MIGRATOR_CLIENT_ID" ./db-migrate migrate "$db;Database=$PGDATABASE;Username=$MIGRATOR_NAME"
'''

resource dbJob 'Microsoft.App/jobs@2024-03-01' = {
  name: 'job-db-${env}-${suffix}' // job names are at most 32 characters
  location: location
  tags: tags
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${dbAdmin.id}': {}, '${dbMigrator.id}': {} } }
  properties: {
    environmentId: jobsEnv.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1200
      replicaRetryLimit: 0 // a failed migration is looked at, never retried blindly
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
    }
    template: {
      containers: [{
        name: 'db'
        image: dbJobImage
        command: ['bash', '-c', dbJobScript]
        resources: { cpu: json('0.5'), memory: '1Gi' }
        env: [
          { name: 'PGHOST', value: postgres.outputs.fqdn }
          { name: 'PGDATABASE', value: databaseName }
          { name: 'STORAGE', value: storage.name }
          { name: 'ADMIN_CLIENT_ID', value: dbAdmin.properties.clientId }
          { name: 'ADMIN_NAME', value: dbAdmin.name }
          { name: 'MIGRATOR_CLIENT_ID', value: dbMigrator.properties.clientId }
          { name: 'MIGRATOR_NAME', value: dbMigrator.name }
          { name: 'MIGRATOR_OBJECT_ID', value: dbMigrator.properties.principalId }
          { name: 'APP_NAME', value: appName }
          { name: 'APP_OBJECT_ID', value: app.identity.principalId }
          { name: 'DOTNET_SYSTEM_GLOBALIZATION_INVARIANT', value: '1' }
        ]
      }]
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
output storageAccount string = storage.name
output dbJob string = dbJob.name
