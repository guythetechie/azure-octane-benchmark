using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Resources;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using common;
using Flurl;
using LanguageExt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

using static LanguageExt.Prelude;

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

    public static OctaneBenchmarkQueueName GetOctaneBenchmarkQueueName(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        return new OctaneBenchmarkQueueName(configuration.GetNonEmptyValue("SERVICE_BUS_RUN_OCTANE_BENCHMARK_QUEUE_NAME"));
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

    public static QueueOctaneBenchmark GetQueueOctaneBenchmark(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        var client = configuration.TryGetNonEmptyValue("SERVICE_BUS_RUN_OCTANE_BENCHMARK_QUEUE_CONNECTION_STRING")
                             .Map(connectionString => new ServiceBusClient(connectionString))
                             .IfFail(_ => provider.GetRequiredService<ServiceBusClient>())
                             .Run()
                             .ThrowIfFail();

        var queue = provider.GetRequiredService<OctaneBenchmarkQueueName>();

        return (virtualMachine, cancellationToken) => ServiceBusModule.QueueOctaneBenchmark(client, queue, virtualMachine, cancellationToken);
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

        return (virtualMachineName, cancellationToken) => ServiceBusModule.QueueVirtualMachineDeletion(client, queue, virtualMachineName, cancellationToken);
    }

    public static ArmClient GetArmClient(IServiceProvider provider)
    {
        var credential = provider.GetRequiredService<TokenCredential>();

        return new ArmClient(credential);
    }

    public static CreateVirtualMachine GetCreateVirtualMachine(IServiceProvider provider)
    {
        return (virtualMachine, cancellationToken) =>
            from _ in unitAff
            let configuration = provider.GetRequiredService<IConfiguration>()
            let subnetId = configuration.GetNonEmptyValue("VIRTUAL_MACHINE_SUBNET_ID")
            let subnetData = new SubnetData { Id = subnetId }
            from resourceGroup in Aff(async () => await GetResourceGroup(provider, cancellationToken))
            from unit in VirtualMachineModule.CreateVirtualMachine(resourceGroup, subnetData, virtualMachine, cancellationToken)
            select unit;
    }

    public static RunOctaneBenchmark GetRunOctaneBenchmark(IServiceProvider provider)
    {
        return (virtualMachine, diagnosticId, cancellationToken) =>
            from _ in unitAff
            let configuration = provider.GetRequiredService<IConfiguration>()

            let rawBenchmarkScript = configuration.GetNonEmptyValue("BASE_64_RUN_OCTANE_BENCHMARK_SCRIPT")
            let benchmarkScript = new Base64Script(rawBenchmarkScript)

            let storageAccountUrl = configuration.GetSection("AzureWebJobsStorage").GetSection("blobServiceUri").Value
            let artifactsContainerName = configuration.GetNonEmptyValue("STORAGE_ACCOUNT_ARTIFACT_CONTAINER_NAME")
            let benchmarkZipFileName = "benchmark.zip"
            let rawDownloadUri = new Uri(storageAccountUrl).AppendPathSegment(artifactsContainerName).AppendPathSegment(benchmarkZipFileName).ToUri()

            let credential = provider.GetRequiredService<TokenCredential>()
            let blobClient = new BlobClient(rawDownloadUri, credential)
            let authenticatedDownloadUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1))
            let benchmarkUri = new BenchmarkExecutableUri(authenticatedDownloadUri.ToString())

            let rawApplicationInsightsConnectionString = configuration.GetNonEmptyValue("APPLICATIONINSIGHTS_CONNECTION_STRING")
            let applicationInsightsConnectionString = new ApplicationInsightsConnectionString(rawApplicationInsightsConnectionString)

            from resourceGroup in Aff(async () => await GetResourceGroup(provider, cancellationToken))
            from unit in VirtualMachineModule.RunOctaneBenchmark(benchmarkScript, benchmarkUri, diagnosticId, applicationInsightsConnectionString, resourceGroup, virtualMachine, cancellationToken)
            select unit;
    }

    public static DeleteVirtualMachine GetDeleteVirtualMachine(IServiceProvider provider)
    {
        return (virtualMachineName, cancellationToken) =>
            from _ in unitAff
            from resourceGroup in Aff(async () => await GetResourceGroup(provider, cancellationToken))
            from unit in VirtualMachineModule.DeleteVirtualMachine(resourceGroup, virtualMachineName, cancellationToken)
            select unit;
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