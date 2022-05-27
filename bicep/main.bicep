param storageAccountName string
param artifactStorageContainerName string
param location string = resourceGroup().location
param tags object = {}
param serviceBusName string
param virtualNetworkName string
param bastionName string
param logAnalyticsWorkspaceName string
param applicationInsightsName string
param appServicePlanName string
param functionAppName string

var bastionSubnetName = 'AzureBastionSubnet'
var virtualMachineSubnetName = 'vm-subnet'

resource storageAccount 'Microsoft.Storage/storageAccounts@2021-09-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
  }
}

resource storageBlobServices 'Microsoft.Storage/storageAccounts/blobServices@2021-09-01' = {
  name: 'default'
  parent: storageAccount
}

resource artifactStorageContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-09-01' = {
  name: artifactStorageContainerName
  parent: storageBlobServices
}
resource serviceBus 'Microsoft.ServiceBus/namespaces@2021-11-01' = {
  name: serviceBusName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
  }
}

resource serviceBusCreateVmQueue 'Microsoft.ServiceBus/namespaces/queues@2021-11-01' = {
  name: 'create-vm-queue'
  parent: serviceBus
}

resource serviceBusDeleteVmQueue 'Microsoft.ServiceBus/namespaces/queues@2021-11-01' = {
  name: 'delete-vm-queue'
  parent: serviceBus
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-12-01-preview' = {
  name: logAnalyticsWorkspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  tags: tags
  kind: 'other'
  properties: {
    Application_Type: 'other'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2021-05-01' = {
  name: virtualNetworkName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/20'
      ]
    }
    subnets: [
      {
        name: bastionSubnetName
        properties: {
          addressPrefix: '10.0.0.0/27'
        }
      }
      {
        name: virtualMachineSubnetName
        properties: {
          addressPrefix: '10.0.1.0/24'
        }
      }
    ]
  }
}

resource bastionSubnet 'Microsoft.Network/virtualNetworks/subnets@2021-05-01' existing = {
  name: bastionSubnetName
  parent: virtualNetwork
}

resource virtualMachineSubnet 'Microsoft.Network/virtualNetworks/subnets@2021-05-01' existing = {
  name: virtualMachineSubnetName
  parent: virtualNetwork
}

// resource bastionPublicIP 'Microsoft.Network/publicIPAddresses@2021-05-01' = {
//   name: '${bastionName}-pip'
//   location: location
//   tags: tags
//   sku: {
//     name: 'Standard'
//   }
//   properties: {
//     publicIPAllocationMethod: 'Static'
//   }
// }

// resource bastion 'Microsoft.Network/bastionHosts@2021-05-01' = {
//   name: bastionName
//   location: location
//   tags: tags
//   properties: {
//     ipConfigurations: [
//       {
//         name: 'ip-configuration'
//         properties: {
//           privateIPAllocationMethod: 'Dynamic'
//           subnet: {
//             id: bastionSubnet.id
//           }
//           publicIPAddress: {
//             id: bastionPublicIP.id
//           }
//         }
//       }
//     ]
//   }
// }

resource appServicePlan 'Microsoft.Web/serverfarms@2021-03-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  sku: {
    tier: 'Dynamic'
    name: 'Y1'
  }
}

resource functionApp 'Microsoft.Web/sites@2021-03-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
  }
}

resource functionAppSettings 'Microsoft.Web/sites/config@2021-02-01' = {
  name: 'appsettings'
  parent: functionApp
  properties: {
    APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsights.properties.ConnectionString
    AzureWebJobsDisableHomepage: 'true'
    AzureWebJobsStorage__blobServiceUri: storageAccount.properties.primaryEndpoints.blob
    FUNCTIONS_WORKER_RUNTIME: 'dotnet'
    FUNCTIONS_EXTENSION_VERSION: '~4'
    VIRTUAL_MACHINE_RESOURCE_GROUP_NAME: resourceGroup().name
    VIRTUAL_MACHINE_SUBNET_ID: virtualMachineSubnet.id
    ServiceBusConnection__fullyQualifiedNamespace: split(split(serviceBus.properties.serviceBusEndpoint, '/')[2], ':')[0]
    SERVICE_BUS_CREATE_VM_QUEUE_NAME: serviceBusCreateVmQueue.name
    SERVICE_BUS_DELETE_VM_QUEUE_NAME: serviceBusDeleteVmQueue.name
  }
}

resource contributorRoleDefinition 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: subscription()
  name: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
}

resource functionAppContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(functionApp.id, resourceGroup().id, contributorRoleDefinition.id)
  scope: resourceGroup()
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: contributorRoleDefinition.id
    principalType: 'ServicePrincipal'
  }
}

resource storageBlobDataOwnerRoleDefinition 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: subscription()
  name: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
}

resource functionAppStorageBlobDataOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(functionApp.id, storageAccount.id, storageBlobDataOwnerRoleDefinition.id)
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: storageBlobDataOwnerRoleDefinition.id
    principalType: 'ServicePrincipal'
  }
}

resource serviceBusDataSenderRoleDefinition 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: subscription()
  name: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
}

resource functionAppServiceBusDataSenderRoleAssignment 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(functionApp.id, serviceBus.id, serviceBusDataSenderRoleDefinition.id)
  scope: serviceBus
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: serviceBusDataSenderRoleDefinition.id
    principalType: 'ServicePrincipal'
  }
}

resource serviceBusDataReceiverRoleDefinition 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: subscription()
  name: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
}

resource functionAppServiceBusDataReceiverRoleAssignment 'Microsoft.Authorization/roleAssignments@2020-10-01-preview' = {
  name: guid(functionApp.id, serviceBus.id, serviceBusDataReceiverRoleDefinition.id)
  scope: serviceBus
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: serviceBusDataReceiverRoleDefinition.id
    principalType: 'ServicePrincipal'
  }
}
