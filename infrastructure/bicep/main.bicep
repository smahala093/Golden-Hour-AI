targetScope = 'resourceGroup'

@description('Short deployment stage used in resource names and configuration.')
@allowed([
  'dev'
  'test'
  'staging'
  'prod'
])
param environmentName string = 'dev'

@description('Azure region. Use a region that supports every selected service/SKU.')
param location string = resourceGroup().location

@description('Lowercase product prefix. Global resource names are normalized and suffixed automatically.')
@minLength(3)
@maxLength(16)
param namePrefix string = 'goldenhour'

@description('Bootstrap or release container image. Deployment replaces the default after the ACR build.')
param containerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('PostgreSQL administrator login. Do not use a personal identity.')
@minLength(1)
param postgresAdministratorLogin string = 'goldenhouradmin'

@secure()
@description('PostgreSQL administrator password supplied only at deployment time.')
param postgresAdministratorPassword string

@secure()
@description('High-entropy application JWT signing key supplied only at deployment time.')
param jwtSigningKey string

@secure()
@description('HMAC secret for provider webhook verification.')
param webhookSigningSecret string

@secure()
@description('Optional OpenAI API key. Leave empty while deterministic mocks are enabled.')
param openAiApiKey string = ''

@secure()
@description('SMS gateway HMAC signing secret. Required only when deterministic provider mocks are disabled.')
param smsGatewaySigningSecret string = ''

@secure()
@description('Optional development-only seeded account password. It is ignored in Production and prod deployments.')
param demoPassword string = ''

@description('Use deterministic provider implementations. Keep enabled unless all real-provider configuration is reviewed.')
param useMockProviders bool = true

@description('Backend OpenAI model configuration.')
param openAiModel string = 'gpt-4.1-mini'

@description('HTTPS SMS gateway message endpoint. Required only when deterministic provider mocks are disabled.')
param smsGatewayEndpoint string = ''

@description('Default country-specific emergency call number.')
param emergencyNumber string = '112'

@description('Application JWT issuer.')
param jwtIssuer string = 'GoldenHourAI'

@description('Application JWT audience.')
param jwtAudience string = 'GoldenHourAI.Web'

@description('Prototype API replica count. Keep this at one until the application is wired to Azure SignalR and a distributed outbox lease.')
@allowed([1])
param minReplicas int = 1
@allowed([1])
param maxReplicas int = 1

@description('Resource tags merged with the standard application/environment tags.')
param tags object = {}

var suffix = take(uniqueString(subscription().subscriptionId, resourceGroup().id, environmentName), 7)
var normalizedPrefix = replace(replace(toLower(namePrefix), '-', ''), '_', '')
var stem = '${normalizedPrefix}-${environmentName}-${suffix}'
var compactStem = '${normalizedPrefix}${environmentName}${suffix}'
var commonTags = union(tags, {
  application: 'Golden Hour AI'
  environment: environmentName
  managedBy: 'Bicep'
  dataClassification: 'sensitive-emergency'
})
var appName = take('${stem}-app', 32)
var acrName = take('${compactStem}acr', 50)
var storageName = take('${compactStem}st', 24)
var keyVaultName = take('${compactStem}kv', 24)
var postgresName = take('${stem}-pg', 63)
var serviceBusName = take('${stem}-sb', 50)
var signalRName = take('${stem}-signalr', 63)
var databaseName = 'goldenhour'
var shouldConfigureOpenAi = !empty(openAiApiKey) && !useMockProviders
var shouldConfigureSmsGateway = !useMockProviders
var shouldSeedDemo = environmentName != 'prod' && !empty(demoPassword)
var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource network 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${stem}-vnet'
  location: location
  tags: commonTags
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.42.0.0/16'
      ]
    }
    subnets: [
      {
        name: 'container-apps'
        properties: {
          addressPrefix: '10.42.0.0/23'
          delegations: [
            {
              name: 'container-apps-delegation'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'postgresql'
        properties: {
          addressPrefix: '10.42.2.0/24'
          delegations: [
            {
              name: 'postgresql-delegation'
              properties: {
                serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
              }
            }
          ]
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource containerAppsSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: network
  name: 'container-apps'
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: network
  name: 'postgresql'
}

resource postgresPrivateDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: '${environmentName}.${normalizedPrefix}.postgres.database.azure.com'
  location: 'global'
  tags: commonTags
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: postgresPrivateDns
  name: '${stem}-pg-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: network.id
    }
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${stem}-logs'
  location: location
  tags: commonTags
  properties: {
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
  sku: {
    name: 'PerGB2018'
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${stem}-appi'
  location: location
  kind: 'web'
  tags: commonTags
  properties: {
    Application_Type: 'web'
    DisableIpMasking: false
    DisableLocalAuth: false
    IngestionMode: 'LogAnalytics'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${stem}-identity'
  location: location
  tags: commonTags
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  tags: commonTags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    dataEndpointEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource registryPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, appIdentity.id, acrPullRoleDefinitionId)
  scope: registry
  properties: {
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: commonTags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource privateUploads 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'emergency-uploads'
  properties: {
    publicAccess: 'None'
  }
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: serviceBusName
  location: location
  tags: commonTags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    disableLocalAuth: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    zoneRedundant: false
  }
}

resource outboxQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBus
  name: 'outbox'
  properties: {
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P14D'
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    enableBatchedOperations: true
    enablePartitioning: true
    lockDuration: 'PT1M'
    maxDeliveryCount: 10
    requiresDuplicateDetection: true
    requiresSession: false
  }
}

resource serviceBusAppRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2024-01-01' = {
  parent: serviceBus
  name: 'golden-hour-app'
  properties: {
    rights: [
      'Listen'
      'Send'
    ]
  }
}

resource signalR 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: signalRName
  location: location
  kind: 'SignalR'
  tags: commonTags
  sku: {
    name: environmentName == 'prod' ? 'Standard_S1' : 'Free_F1'
    tier: environmentName == 'prod' ? 'Standard' : 'Free'
    capacity: 1
  }
  properties: {
    disableAadAuth: false
    disableLocalAuth: false
    features: [
      {
        flag: 'ServiceMode'
        value: 'Default'
        properties: {}
      }
      {
        flag: 'EnableConnectivityLogs'
        value: 'true'
        properties: {}
      }
      {
        flag: 'EnableMessagingLogs'
        value: 'false'
        properties: {}
      }
    ]
    publicNetworkAccess: 'Enabled'
    tls: {
      clientCertEnabled: false
    }
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresName
  location: location
  tags: commonTags
  sku: {
    name: environmentName == 'prod' ? 'Standard_D2ds_v5' : 'Standard_B1ms'
    tier: environmentName == 'prod' ? 'GeneralPurpose' : 'Burstable'
  }
  properties: {
    administratorLogin: postgresAdministratorLogin
    administratorLoginPassword: postgresAdministratorPassword
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
    backup: {
      backupRetentionDays: environmentName == 'prod' ? 14 : 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      delegatedSubnetResourceId: postgresSubnet.id
      privateDnsZoneArmResourceId: postgresPrivateDns.id
      publicNetworkAccess: 'Disabled'
    }
    storage: {
      autoGrow: 'Enabled'
      storageSizeGB: 32
    }
    version: '16'
  }
  dependsOn: [
    postgresDnsLink
  ]
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: commonTags
  properties: {
    accessPolicies: []
    enablePurgeProtection: environmentName == 'prod'
    enableRbacAuthorization: true
    enableSoftDelete: true
    publicNetworkAccess: 'Enabled'
    sku: {
      family: 'A'
      name: 'standard'
    }
    softDeleteRetentionInDays: 7
    tenantId: tenant().tenantId
  }
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, appIdentity.id, keyVaultSecretsUserRoleDefinitionId)
  scope: keyVault
  properties: {
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
  }
}

var postgresConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${postgresAdministratorLogin};Password=${postgresAdministratorPassword};SSL Mode=Require;Trust Server Certificate=false'
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
var serviceBusConnectionString = serviceBusAppRule.listKeys().primaryConnectionString
var signalRConnectionString = signalR.listKeys().primaryConnectionString

resource postgresConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'postgres-connection'
  properties: {
    value: postgresConnectionString
  }
}

resource jwtSigningSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'jwt-signing-key'
  properties: {
    value: jwtSigningKey
  }
}

resource webhookSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'webhook-signing-secret'
  properties: {
    value: webhookSigningSecret
  }
}

resource storageConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'storage-connection'
  properties: {
    value: storageConnectionString
  }
}

resource serviceBusConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'service-bus-connection'
  properties: {
    value: serviceBusConnectionString
  }
}

resource signalRConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'signalr-connection'
  properties: {
    value: signalRConnectionString
  }
}

resource appInsightsConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'application-insights-connection'
  properties: {
    value: applicationInsights.properties.ConnectionString
  }
}

resource openAiSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (shouldConfigureOpenAi) {
  parent: keyVault
  name: 'openai-api-key'
  properties: {
    value: openAiApiKey
  }
}

resource smsGatewaySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (shouldConfigureSmsGateway) {
  parent: keyVault
  name: 'sms-gateway-signing-secret'
  properties: {
    value: smsGatewaySigningSecret
  }
}

resource demoPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (shouldSeedDemo) {
  parent: keyVault
  name: 'demo-password'
  properties: {
    value: demoPassword
  }
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${stem}-cae'
  location: location
  tags: commonTags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: containerAppsSubnet.id
      internal: false
    }
    zoneRedundant: false
  }
  dependsOn: [
    network
  ]
}

var baseContainerSecrets = [
  {
    name: 'postgres-connection'
    keyVaultUrl: postgresConnectionSecret.properties.secretUriWithVersion
    identity: appIdentity.id
  }
  {
    name: 'jwt-signing-key'
    keyVaultUrl: jwtSigningSecret.properties.secretUriWithVersion
    identity: appIdentity.id
  }
  {
    name: 'webhook-signing-secret'
    keyVaultUrl: webhookSecret.properties.secretUriWithVersion
    identity: appIdentity.id
  }
  {
    name: 'storage-connection'
    keyVaultUrl: storageConnectionSecret.properties.secretUriWithVersion
    identity: appIdentity.id
  }
  {
    name: 'service-bus-connection'
    keyVaultUrl: serviceBusConnectionSecret.properties.secretUriWithVersion
    identity: appIdentity.id
  }
  {
    name: 'signalr-connection'
    keyVaultUrl: signalRConnectionSecret.properties.secretUriWithVersion
    identity: appIdentity.id
  }
  {
    name: 'application-insights-connection'
    keyVaultUrl: appInsightsConnectionSecret.properties.secretUriWithVersion
    identity: appIdentity.id
  }
]
var optionalOpenAiContainerSecrets = shouldConfigureOpenAi ? [
  {
    name: 'openai-api-key'
    keyVaultUrl: openAiSecret.properties.secretUriWithVersion
    identity: appIdentity.id
  }
] : []
var optionalSmsGatewayContainerSecrets = shouldConfigureSmsGateway ? [
  {
    name: 'sms-gateway-signing-secret'
    keyVaultUrl: smsGatewaySecret.properties.secretUriWithVersion
    identity: appIdentity.id
  }
] : []
var optionalDemoContainerSecrets = shouldSeedDemo ? [
  {
    name: 'demo-password'
    keyVaultUrl: demoPasswordSecret.properties.secretUriWithVersion
    identity: appIdentity.id
  }
] : []
var containerSecrets = concat(baseContainerSecrets, optionalOpenAiContainerSecrets, optionalSmsGatewayContainerSecrets, optionalDemoContainerSecrets)

var baseEnvironmentVariables = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: environmentName == 'prod' ? 'Production' : 'Staging'
  }
  {
    name: 'ASPNETCORE_HTTP_PORTS'
    value: '8080'
  }
  {
    name: 'Database__UseInMemory'
    value: 'false'
  }
  {
    name: 'Database__MigrateOnStartup'
    value: 'false'
  }
  {
    name: 'Security__UseForwardedHeaders'
    value: 'true'
  }
  {
    name: 'Security__KnownNetworks__0'
    value: '10.42.0.0/16'
  }
  {
    name: 'ConnectionStrings__GoldenHour'
    secretRef: 'postgres-connection'
  }
  {
    name: 'Authentication__Jwt__Issuer'
    value: jwtIssuer
  }
  {
    name: 'Authentication__Jwt__Audience'
    value: jwtAudience
  }
  {
    name: 'Authentication__Jwt__SigningKey'
    secretRef: 'jwt-signing-key'
  }
  {
    name: 'Authentication__AccessTokenMinutes'
    value: '10'
  }
  {
    name: 'Authentication__RefreshTokenDays'
    value: '7'
  }
  {
    name: 'Webhook__SigningSecret'
    secretRef: 'webhook-signing-secret'
  }
  {
    name: 'Providers__UseMocks'
    value: string(useMockProviders)
  }
  {
    name: 'OpenAI__Model'
    value: openAiModel
  }
  {
    name: 'Emergency__DefaultNumber'
    value: emergencyNumber
  }
  {
    name: 'Emergency__AiConfidenceThreshold'
    value: '0.7'
  }
  {
    name: 'Azure__Storage__ConnectionString'
    secretRef: 'storage-connection'
  }
  {
    name: 'Azure__ServiceBus__ConnectionString'
    secretRef: 'service-bus-connection'
  }
  {
    name: 'Azure__SignalR__ConnectionString'
    secretRef: 'signalr-connection'
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    secretRef: 'application-insights-connection'
  }
]
var optionalOpenAiEnvironmentVariables = shouldConfigureOpenAi ? [
  {
    name: 'OpenAI__ApiKey'
    secretRef: 'openai-api-key'
  }
] : []
var optionalSmsGatewayEnvironmentVariables = shouldConfigureSmsGateway ? [
  {
    name: 'SmsGateway__Enabled'
    value: 'true'
  }
  {
    name: 'SmsGateway__Endpoint'
    value: smsGatewayEndpoint
  }
  {
    name: 'SmsGateway__SigningSecret'
    secretRef: 'sms-gateway-signing-secret'
  }
  {
    name: 'SmsGateway__TimeoutSeconds'
    value: '10'
  }
  {
    name: 'SmsGateway__MaximumResponseBytes'
    value: '65536'
  }
] : []
var optionalDemoEnvironmentVariables = shouldSeedDemo ? [
  {
    name: 'Seed__DemoPassword'
    secretRef: 'demo-password'
  }
] : []
var environmentVariables = concat(baseEnvironmentVariables, optionalOpenAiEnvironmentVariables, optionalSmsGatewayEnvironmentVariables, optionalDemoEnvironmentVariables)

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentity.id}': {}
    }
  }
  properties: {
    configuration: {
      activeRevisionsMode: 'Multiple'
      ingress: {
        allowInsecure: false
        external: true
        targetPort: 8080
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
        transport: 'auto'
      }
      registries: [
        {
          identity: appIdentity.id
          server: registry.properties.loginServer
        }
      ]
      secrets: containerSecrets
    }
    environmentId: containerEnvironment.id
    template: {
      containers: [
        {
          name: 'golden-hour'
          image: containerImage
          env: environmentVariables
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 20
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 10
              timeoutSeconds: 5
              failureThreshold: 6
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-concurrency'
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
  dependsOn: [
    registryPull
    keyVaultSecretsUser
  ]
}

resource migrationJob 'Microsoft.App/jobs@2024-03-01' = {
  name: take('${stem}-migrate', 32)
  location: location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentity.id}': {}
    }
  }
  properties: {
    configuration: {
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          identity: appIdentity.id
          server: registry.properties.loginServer
        }
      ]
      replicaRetryLimit: 1
      replicaTimeout: 900
      secrets: [
        {
          name: 'postgres-connection'
          keyVaultUrl: postgresConnectionSecret.properties.secretUriWithVersion
          identity: appIdentity.id
        }
      ]
      triggerType: 'Manual'
    }
    environmentId: containerEnvironment.id
    template: {
      containers: [
        {
          name: 'ef-migrations'
          image: containerImage
          command: [
            '/app/efbundle'
          ]
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: environmentName == 'prod' ? 'Production' : 'Staging'
            }
            {
              name: 'ConnectionStrings__GoldenHour'
              secretRef: 'postgres-connection'
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
  dependsOn: [
    registryPull
    keyVaultSecretsUser
  ]
}

output applicationName string = containerApp.name
output applicationUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output containerRegistryName string = registry.name
output containerRegistryLoginServer string = registry.properties.loginServer
output keyVaultResourceName string = keyVault.name
output migrationJobName string = migrationJob.name
output postgresServerName string = postgres.name
output resourceSuffix string = suffix
