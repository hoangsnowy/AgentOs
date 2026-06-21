@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param administratorLogin string

@secure()
param administratorLoginPassword string

param postgres_kv_outputs_name string

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: take('postgres-${uniqueString(resourceGroup().id)}', 63)
  location: location
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
    availabilityZone: '1'
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    storage: {
      storageSizeGB: 32
    }
    version: '16'
  }
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  tags: {
    'aspire-resource-name': 'postgres'
    'pw-rotation': '2'
  }
}

resource postgreSqlFirewallRule_AllowAllAzureIps 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  name: 'AllowAllAzureIps'
  properties: {
    endIpAddress: '0.0.0.0'
    startIpAddress: '0.0.0.0'
  }
  parent: postgres
}

resource DefaultConnection 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  name: 'agentos'
  parent: postgres
}

resource keycloak_db 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  name: 'keycloak2'
  parent: postgres
}

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: postgres_kv_outputs_name
}

resource connectionString 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  name: 'connectionstrings--postgres'
  properties: {
    value: 'Host=${postgres.properties.fullyQualifiedDomainName};Username=${administratorLogin};Password=${administratorLoginPassword}'
  }
  parent: keyVault
}

resource DefaultConnection_connectionString 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  name: 'connectionstrings--DefaultConnection'
  properties: {
    value: 'Host=${postgres.properties.fullyQualifiedDomainName};Username=${administratorLogin};Password=${administratorLoginPassword};Database=agentos'
  }
  parent: keyVault
}

resource keycloak_db_connectionString 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  name: 'connectionstrings--keycloak-db'
  properties: {
    value: 'Host=${postgres.properties.fullyQualifiedDomainName};Username=${administratorLogin};Password=${administratorLoginPassword};Database=keycloak'
  }
  parent: keyVault
}

output name string = postgres.name

output id string = postgres.id

output hostName string = postgres.properties.fullyQualifiedDomainName