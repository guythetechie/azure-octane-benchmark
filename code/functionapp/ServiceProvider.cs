using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Resources;
using common;
using LanguageExt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;

public record AzureAuthorityUri : UriRecord
{
    public AzureAuthorityUri(string value) : base(value) { }
}

public static class ServiceProviderModule
{
    public static AzureAuthorityUri GetAzureAuthorityUri(IServiceProvider provider)
    {
        var uriOption = from configuration in provider.GetOptionalService<IConfiguration>()
                        from environment in configuration.GetOptionalValue("AZURE_ENVIRONMENT")
                        select
                          environment switch
                          {
                              nameof(AzureAuthorityHosts.AzureChina) => AzureAuthorityHosts.AzureChina,
                              nameof(AzureAuthorityHosts.AzurePublicCloud) => AzureAuthorityHosts.AzurePublicCloud,
                              nameof(AzureAuthorityHosts.AzureGermany) => AzureAuthorityHosts.AzureGermany,
                              nameof(AzureAuthorityHosts.AzureGovernment) => AzureAuthorityHosts.AzureGovernment,
                              _ => throw new InvalidOperationException($"'{environment}' is not a valid Azure Authority host.")
                          };

        var uri = uriOption.IfNone(AzureAuthorityHosts.AzurePublicCloud);

        return new AzureAuthorityUri(uri.ToString());
    }

    public static TokenCredential GetTokenCredential(IServiceProvider provider)
    {
        var authority = provider.GetRequiredService<AzureAuthorityUri>();
        var options = new DefaultAzureCredentialOptions()
        {
            AuthorityHost = authority.ToUri()
        };

        return new DefaultAzureCredential(options);
    }

    public static ServiceBusClient GetServiceBusClient(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        return configuration.TryGetNonEmptyValue("SERVICE_BUS_CONNECTION_STRING")
                            .Map(connectionString => new ServiceBusClient(connectionString))
                            .IfFail(_ => GetServiceBusClientFromTokenCredential(provider))
                            .Run()
                            .ThrowIfFail();
    }

    public static VirtualMachineCreationQueueName GetVirtualMachineCreationQueueName(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        return new VirtualMachineCreationQueueName(configuration.GetNonEmptyValue("SERVICE_BUS_CREATE_VM_QUEUE_NAME"));
    }

    public static VirtualMachineDeletionQueueName GetVirtualMachineDeletionQueueName(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        return new VirtualMachineDeletionQueueName(configuration.GetNonEmptyValue("SERVICE_BUS_DELETE_VM_QUEUE_NAME"));
    }

    public static QueueVirtualMachineCreation GetQueueVirtualMachineCreation(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        var client = configuration.TryGetNonEmptyValue("SERVICE_BUS_CREATE_VM_QUEUE_CONNECTION_STRING")
                             .Map(connectionString => new ServiceBusClient(connectionString))
                             .IfFail(_ => provider.GetRequiredService<ServiceBusClient>())
                             .Run()
                             .ThrowIfFail();

        var queue = provider.GetRequiredService<VirtualMachineCreationQueueName>();

        return (virtualMachines, cancellationToken) => ServiceBusModule.QueueVirtualMachineCreation(client, queue, virtualMachines, cancellationToken);
    }

    public static QueueVirtualMachineDeletion GetQueueVirtualMachineDeletion(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        var client = configuration.TryGetNonEmptyValue("SERVICE_BUS_DELETE_VM_QUEUE_CONNECTION_STRING")
                             .Map(connectionString => new ServiceBusClient(connectionString))
                             .IfFail(_ => provider.GetRequiredService<ServiceBusClient>())
                             .Run()
                             .ThrowIfFail();

        var queue = provider.GetRequiredService<VirtualMachineDeletionQueueName>();

        return (virtualMachine, cancellationToken) => ServiceBusModule.QueueVirtualMachineDeletion(client, queue, virtualMachine, cancellationToken);
    }

    public static ArmClient GetArmClient(IServiceProvider provider)
    {
        var credential = provider.GetRequiredService<TokenCredential>();

        return new ArmClient(credential);
    }

    public static CreateVirtualMachine GetCreateVirtualMachine(IServiceProvider provider)
    {
        return async (virtualMachine, cancellationToken) =>
        {
            var resourceGroup = await GetResourceGroup(provider, cancellationToken);

            var configuration = provider.GetRequiredService<IConfiguration>();
            var subnetId = configuration.GetNonEmptyValue("VIRTUAL_MACHINE_SUBNET_ID");
            var subnetData = new SubnetData { Id = subnetId };

            return await VirtualMachineModule.CreateVirtualMachine(resourceGroup, subnetData, virtualMachine, cancellationToken);
        };
    }

    public static DeleteVirtualMachine GetDeleteVirtualMachine(IServiceProvider provider)
    {
        return async (virtualMachineName, cancellationToken) =>
        {
            var resourceGroup = await GetResourceGroup(provider, cancellationToken);

            return await VirtualMachineModule.DeleteVirtualMachine(resourceGroup, virtualMachineName, cancellationToken);
        };
    }

    private static async ValueTask<ResourceGroupResource> GetResourceGroup(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var armClient = provider.GetRequiredService<ArmClient>();
        var subscription = await armClient.GetDefaultSubscriptionAsync(cancellationToken);

        var configuration = provider.GetRequiredService<IConfiguration>();
        var resourceGroupName = configuration.GetNonEmptyValue("VIRTUAL_MACHINE_RESOURCE_GROUP_NAME");

        return await subscription.GetResourceGroupAsync(resourceGroupName, cancellationToken);
    }

    private static ServiceBusClient GetServiceBusClientFromTokenCredential(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var serviceBusNamespace = configuration.GetSection("ServiceBusConnection").GetSection("fullyQualifiedNamespace").Value;
        var credential = provider.GetRequiredService<TokenCredential>();

        return new ServiceBusClient(serviceBusNamespace, credential);
    }
}