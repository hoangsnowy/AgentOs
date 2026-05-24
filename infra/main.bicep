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

@description('Image full reference (vd <acr>.azurecr.io/agenticsdlc:abc123).')
param containerImage string

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

// ---- Names (deterministic) ----

var suffix = uniqueString(resourceGroup().id, appName, environmentName)
var acrName = toLower('${appName}acr${suffix}')
var lawName = '${appName}-law-${environmentName}'
var aiName = '${appName}-ai-${environmentName}'
var caeName = '${appName}-cae-${environmentName}'
var caName = '${appName}-${environmentName}'
var kvName = toLower('${appName}-kv-${take(suffix, 8)}')
var idName = '${appName}-id-${environmentName}'

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
resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
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
resource kvSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, 'KeyVaultSecretsUser')
  scope: keyVault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    // Key Vault Secrets User built-in role
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
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

// ---- Container App ----

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
      secrets: [
        {
          name: 'appinsights-connection-string'
          value: appInsights.properties.ConnectionString
        }
      ]
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
          env: [
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
          ]
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
output acrLoginServer string = acr.properties.loginServer
output keyVaultUri string = keyVault.properties.vaultUri
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
output appInsightsConnectionString string = appInsights.properties.ConnectionString
