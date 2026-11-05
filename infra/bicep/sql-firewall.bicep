// One SQL firewall rule per address. A module because the list (the web app's outbound addresses) is only known mid-deployment.
param serverName string
param addresses array

resource sql 'Microsoft.Sql/servers@2023-08-01-preview' existing = { name: serverName }

resource rules 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = [for (ip, i) in addresses: {
  parent: sql
  name: 'app-outbound-${i}'
  properties: { startIpAddress: ip, endIpAddress: ip }
}]
