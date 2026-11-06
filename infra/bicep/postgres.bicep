// MDV-P0-038: one PostgreSQL Flexible Server per country deployment (ADR-012). Replaces the Azure SQL server and database.
// Private access only (VNet-integrated, public network access disabled), Entra-only login, storage encrypted with a customer-managed key.
param name string
param location string
param tags object
@description('ISO country of this deployment. Backups, HA and restore must stay inside it (AGENTS.md 1.6).')
@allowed(['IN', 'AE'])
param country string = 'IN'
param databaseName string = 'medvya'
@description('Subnet delegated to Microsoft.DBforPostgreSQL/flexibleServers. The server has no other network path.')
param delegatedSubnetId string
@description('Private DNS zone (name ends .postgres.database.azure.com), already linked to the VNet.')
param privateDnsZoneId string
@description('Entra object id and sign-in name of the human or group that administers the server. No password administrator exists.')
param entraAdminObjectId string
param entraAdminLogin string
@allowed(['User', 'Group', 'ServicePrincipal'])
param entraAdminType string = 'User'
@description('Resource id of the user-assigned identity that wraps and unwraps the storage key (needs Key Vault Crypto Service Encryption User on the key).')
param cmkIdentityId string
@description('Versionless Key Vault key URI: new key versions rotate in without a deployment.')
param cmkKeyUri string
@description('Zone-redundant HA. On for production; off for dev (Burstable tiers do not support it).')
param haEnabled bool = false
@description('Point-in-time restore window in days (7 to 35). Production 14, dev 7.')
@minValue(7)
@maxValue(35)
param pitrRetentionDays int = 7
@description('Copy backups to the Azure paired region. Allowed only when that region is in the same country (India: Central/South; UAE: North/Central).')
param geoRedundantBackup bool = false
param skuName string = 'Standard_B1ms'
@allowed(['Burstable', 'GeneralPurpose', 'MemoryOptimized'])
param skuTier string = 'Burstable'
param storageSizeGB int = 32
param logAnalyticsId string
@description('pgaudit classes. read, write and ddl cover every statement that touches or changes data; role covers grants.')
param pgauditLog string = 'read, write, ddl, role'

// Azure fixes the geo-backup target as the paired region, so each region maps to its own country and its pair's country.
var regions = {
  centralindia: { country: 'IN', pairCountry: 'IN' } // pair: southindia
  southindia: { country: 'IN', pairCountry: 'IN' } // pair: centralindia
  uaenorth: { country: 'AE', pairCountry: 'AE' } // pair: uaecentral
  uaecentral: { country: 'AE', pairCountry: 'AE' } // pair: uaenorth
}
var region = regions[?toLower(location)]
// Fails the build and the deployment for an unknown region, a region outside `country`, or a backup pair outside it.
var regionOk = region != null && region!.country == country && (!geoRedundantBackup || region!.pairCountry == country)
var checkedLocation = regionOk ? location : fail('Region ${location} (or its backup pair) is not in country ${country}: backups must stay in country.')

// pgcrypto for application hashing, pg_stat_statements for query stats, pgaudit for the audit trail. uuid-ossp is not needed.
var serverSettings = [
  { name: 'azure.extensions', value: 'PGCRYPTO,PG_STAT_STATEMENTS,PGAUDIT' }
  { name: 'shared_preload_libraries', value: 'pgaudit,pg_stat_statements' } // takes effect at the next restart; restart the server once after the first deployment
  { name: 'pgaudit.log', value: pgauditLog }
  { name: 'require_secure_transport', value: 'on' }
  { name: 'ssl_min_protocol_version', value: 'TLSv1.2' }
  { name: 'log_connections', value: 'on' }
  { name: 'log_disconnections', value: 'on' }
]

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'psql-${name}'
  location: checkedLocation
  tags: tags
  sku: { name: skuName, tier: skuTier }
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${cmkIdentityId}': {} } }
  properties: {
    version: '16'
    createMode: 'Default'
    authConfig: { activeDirectoryAuth: 'Enabled', passwordAuth: 'Disabled', tenantId: tenant().tenantId }
    network: { delegatedSubnetResourceId: delegatedSubnetId, privateDnsZoneArmResourceId: privateDnsZoneId, publicNetworkAccess: 'Disabled' }
    dataEncryption: { type: 'AzureKeyVault', primaryKeyURI: cmkKeyUri, primaryUserAssignedIdentityId: cmkIdentityId }
    storage: { storageSizeGB: storageSizeGB, autoGrow: 'Enabled' }
    backup: { backupRetentionDays: pitrRetentionDays, geoRedundantBackup: geoRedundantBackup ? 'Enabled' : 'Disabled' }
    highAvailability: { mode: haEnabled ? 'ZoneRedundant' : 'Disabled' }
  }
}

// The only login the server has. The app's managed identity is mapped to a least-privilege role by this administrator
// (pgaadauth_create_principal), never made an administrator.
resource admin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = {
  parent: server
  name: entraAdminObjectId
  properties: { principalType: entraAdminType, principalName: entraAdminLogin, tenantId: tenant().tenantId }
}

// Server parameters are applied one at a time (Azure rejects parallel changes) and after the administrator exists.
@batchSize(1)
resource settings 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = [for s in serverSettings: {
  parent: server
  name: s.name
  properties: { value: s.value, source: 'user-override' }
  dependsOn: [admin]
}]

resource db 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: databaseName
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
  dependsOn: [settings]
}

resource diag 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: server
  properties: {
    workspaceId: logAnalyticsId
    logs: [{ categoryGroup: 'allLogs', enabled: true }]
    metrics: [{ category: 'AllMetrics', enabled: true }]
  }
}

output fqdn string = server.properties.fullyQualifiedDomainName
output serverName string = server.name
output databaseName string = db.name
