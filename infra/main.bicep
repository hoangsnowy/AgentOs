// infra/main.bicep
// Phase 6 — IaC cho Azure Container Apps deployment của AgenticSdlc.Api.
// Resources: ACR + Log Analytics + App Insights + Container Apps Environment +
//   Key Vault (LLM secrets) + User-Assigned Identity + Container App.

targetScope = 'resourceGroup'

// ---- Parameters ----

@description('Tên app, dùng cho prefix mọi resource (chữ thường, ≤ 12 ký tự).')
@minLength(3)
@maxLength(12)
param appName string = 'agenticsdlc'

@description('Azure region (vd southeastasia, eastus).')
param location string = resourceGroup().location

@description('Environment tag: dev | staging | prod.')
@allowed([
  'dev'
  'staging'
  'prod'
])
param environmentName string = 'dev'

@description('Image full reference cho API (vd <acr>.azurecr.io/agenticsdlc:abc123).')
param containerImage string

@description('Image full reference cho Web/Blazor. Bỏ trống → không deploy Web container (chỉ API).')
param webContainerImage string = ''

@description('CPU cores per replica.')
@allowed([
  '0.25'
  '0.5'
  '0.75'
  '1.0'
  '2.0'
])
param cpu string = '0.5'

@description('Memory per replica.')
@allowed([
  '0.5Gi'
  '1.0Gi'
  '1.5Gi'
  '2.0Gi'
  '4.0Gi'
])
param memory string = '1.0Gi'

@description('Min replicas (scale-to-zero nếu = 0).')
@minValue(0)
@maxValue(10)
param minReplicas int = 0

@description('Max replicas.')
@minValue(1)
@maxValue(30)
param maxReplicas int = 3

@description('Tạo role assignment (AcrPull + KeyVaultSecretsUser cho UAMI). Cần quyền Owner/User Access Administrator. CI chỉ có Contributor → truyền false; bootstrap role 1 lần bằng deploy thủ công.')
param deployRoleAssignments bool = true

@description('Provision Azure Database for PostgreSQL flexible server + wire connection string vào Container App. Default false (tránh phát sinh cost ~$13/tháng). Bật → bắt buộc postgresAdminPassword.')
param deployPostgres bool = false

@description('Postgres admin login (chỉ dùng khi deployPostgres=true).')
param postgresAdminLogin string = 'pgadmin'

@description('Postgres admin password (@secure). Bắt buộc khi deployPostgres=true.')
@secure()
param postgresAdminPassword string = ''

// ---- Names (deterministic) ----

var suffix = uniqueString(resourceGroup().id, appName, environmentName)
var acrName = toLower('${appName}acr${suffix}')
var lawName = '${appName}-law-${environmentName}'
var aiName = '${appName}-ai-${environmentName}'
var caeName = '${appName}-cae-${environmentName}'
var caName = '${appName}-${environmentName}'
var webAppName = '${appName}-web-${environmentName}'
var deployWeb = !empty(webContainerImage)
var kvName = toLower('${appName}-kv-${take(suffix, 8)}')
var idName = '${appName}-id-${environmentName}'
var pgServerName = toLower('${appName}-pg-${take(suffix, 8)}')
var pgDatabaseName = 'agentic_sdlc'

// ---- User-Assigned Managed Identity ----

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: idName
  location: location
}

// ---- Log Analytics + App Insights ----

resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: lawName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: aiName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: law.id
    IngestionMode: 'LogAnalytics'
  }
}

// ---- Azure Container Registry ----

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

// Cho identity quyền AcrPull
resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployRoleAssignments) {
  name: guid(acr.id, identity.id, 'AcrPull')
  scope: acr
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    // AcrPull built-in role id
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

// ---- Key Vault cho LLM secrets ----

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Cho identity quyền đọc secret
resource kvSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployRoleAssignments) {
  name: guid(keyVault.id, identity.id, 'KeyVaultSecretsUser')
  scope: keyVault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    // Key Vault Secrets User built-in role
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

// ---- Postgres flexible server (tuỳ chọn, persistence layer) ----

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = if (deployPostgres) {
  name: pgServerName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
  }
}

resource postgresDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = if (deployPostgres) {
  parent: postgres
  name: pgDatabaseName
}

// Special rule start=end=0.0.0.0 ⇒ cho phép mọi Azure service (Container App) kết nối.
resource postgresFirewallAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = if (deployPostgres) {
  parent: postgres
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ---- Container Apps Environment ----

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: caeName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: law.properties.customerId
        sharedKey: law.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

// Secrets + env dùng chung cho cả API và Web container (cùng engine in-process).
var containerSecrets = concat([
  {
    name: 'appinsights-connection-string'
    value: appInsights.properties.ConnectionString
  }
], deployPostgres ? [
  {
    name: 'db-connection'
    // postgres chỉ deploy khi deployPostgres=true — cùng điều kiện với nhánh này.
    #disable-next-line BCP318
    value: 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${pgDatabaseName};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require;Trust Server Certificate=true'
  }
] : [])

var containerEnv = concat([
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: environmentName == 'prod' ? 'Production' : 'Staging'
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    secretRef: 'appinsights-connection-string'
  }
  {
    name: 'AZURE_CLIENT_ID'
    value: identity.properties.clientId
  }
  {
    name: 'KeyVault__Endpoint'
    value: keyVault.properties.vaultUri
  }
], deployPostgres ? [
  {
    name: 'ConnectionStrings__DefaultConnection'
    secretRef: 'db-connection'
  }
] : [])

// ---- Container App: API (REST + Scalar) ----

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: caName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  dependsOn: [
    acrPullRole
    kvSecretsUserRole
  ]
  properties: {
    managedEnvironmentId: cae.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: '${acrName}.azurecr.io'
          identity: identity.id
        }
      ]
      secrets: containerSecrets
    }
    template: {
      containers: [
        {
          name: appName
          image: containerImage
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: containerEnv
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 15
              periodSeconds: 20
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-rule'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

// ---- Container App: Web (Blazor — Agent Studio UI) ----
// Conditional theo webContainerImage. Chia sẻ CAE/ACR/UAMI/KV/AppInsights với API.

resource webApp 'Microsoft.App/containerApps@2024-03-01' = if (deployWeb) {
  name: webAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  dependsOn: [
    acrPullRole
    kvSecretsUserRole
  ]
  properties: {
    managedEnvironmentId: cae.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: '${acrName}.azurecr.io'
          identity: identity.id
        }
      ]
      secrets: containerSecrets
    }
    template: {
      containers: [
        {
          name: '${appName}-web'
          image: webContainerImage
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: containerEnv
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 15
              periodSeconds: 20
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-rule'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

// ---- Outputs ----

output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
#disable-next-line BCP318
output webAppFqdn string = deployWeb ? webApp.properties.configuration.ingress.fqdn : ''
output acrLoginServer string = acr.properties.loginServer
output keyVaultUri string = keyVault.properties.vaultUri
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
output appInsightsConnectionString string = appInsights.properties.ConnectionString
#disable-next-line BCP318
output postgresFqdn string = deployPostgres ? postgres.properties.fullyQualifiedDomainName : ''
