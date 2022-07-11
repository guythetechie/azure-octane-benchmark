using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Resources;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using common;
using Flurl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;

internal record AzureAuthorityUri : UriRecord
{
    public AzureAuthorityUri(string value) : base(value) { }
}

internal static class ServiceProviderModule
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
            AuthorityHost = authority.Uri
        };

        return new DefaultAzureCredential(options);
    }

    public static ServiceBusClient GetServiceBusClient(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        return configuration.GetOptionalNonEmptyValue("SERVICE_BUS_CONNECTION_STRING")
                            .Map(connectionString => new ServiceBusClient(connectionString))
                            .IfNone(() =>
                            {
                                var configuration = provider.GetRequiredService<IConfiguration>();
                                var serviceBusNamespace = configuration.GetSection("ServiceBusConnection").GetSection("fullyQualifiedNamespace").Value;
                                var credential = provider.GetRequiredService<TokenCredential>();

                                return new ServiceBusClient(serviceBusNamespace, credential);
                            });
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

    public static QueueVirtualMachineCreation QueueVirtualMachineCreation(IServiceProvider provider)
    {
        return async (virtualMachines, cancellationToken) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            var client = configuration.GetOptionalNonEmptyValue("SERVICE_BUS_CREATE_VM_QUEUE_CONNECTION_STRING")
                                      .Map(connectionString => new ServiceBusClient(connectionString))
                                      .IfNone(() => provider.GetRequiredService<ServiceBusClient>());

            var queueName = provider.GetRequiredService<VirtualMachineCreationQueueName>();

            await ServiceBusModule.QueueVirtualMachineCreation(client, queueName, virtualMachines, cancellationToken);
        };
    }

    public static QueueOctaneBenchmark QueueOctaneBenchmark(IServiceProvider provider)
    {
        return async (virtualMachine, cancellationToken) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            var client = configuration.GetOptionalNonEmptyValue("SERVICE_BUS_RUN_OCTANE_BENCHMARK_QUEUE_CONNECTION_STRING")
                                      .Map(connectionString => new ServiceBusClient(connectionString))
                                      .IfNone(() => provider.GetRequiredService<ServiceBusClient>());

            var queueName = provider.GetRequiredService<OctaneBenchmarkQueueName>();

            await ServiceBusModule.QueueOctaneBenchmark(client, queueName, virtualMachine, cancellationToken);
        };
    }

    public static QueueVirtualMachineDeletion QueueVirtualMachineDeletion(IServiceProvider provider)
    {
        return async (virtualMachine, cancellationToken) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            var client = configuration.GetOptionalNonEmptyValue("SERVICE_BUS_DELETE_VM_QUEUE_CONNECTION_STRING")
                                      .Map(connectionString => new ServiceBusClient(connectionString))
                                      .IfNone(() => provider.GetRequiredService<ServiceBusClient>());

            var queueName = provider.GetRequiredService<VirtualMachineDeletionQueueName>();

            await ServiceBusModule.QueueVirtualMachineDeletion(client, queueName, virtualMachine, cancellationToken);
        };
    }

    public static ArmClient GetArmClient(IServiceProvider provider)
    {
        var credential = provider.GetRequiredService<TokenCredential>();

        return new ArmClient(credential);
    }

    public static CreateVirtualMachine CreateVirtualMachine(IServiceProvider provider)
    {
        return async (virtualMachine, cancellationToken) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var subnetId = configuration.GetNonEmptyValue("VIRTUAL_MACHINE_SUBNET_ID");
            var subnetData = new SubnetData { Id = subnetId };
            var resourceGroup = await GetResourceGroup(provider, cancellationToken);

            await VirtualMachineModule.CreateVirtualMachine(resourceGroup, subnetData, virtualMachine, cancellationToken);
        };
    }

    public static RunOctaneBenchmark RunOctaneBenchmark(IServiceProvider provider)
    {
        return async (virtualMachine, diagnosticId, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(System.Random.Shared.NextDouble()), cancellationToken);
            //var configuration = provider.GetRequiredService<IConfiguration>();

            //var base64BenchmarkScript = configuration.GetNonEmptyValue("BASE_64_RUN_OCTANE_BENCHMARK_SCRIPT");
            //var benchmarkScriptBinaryData = new BinaryData(Convert.FromBase64String(base64BenchmarkScript));
            //var benchmarkScript = new BenchmarkScript(benchmarkScriptBinaryData);

            //var applicationInsightsConnectionStringValue = configuration.GetNonEmptyValue("APPLICATIONINSIGHTS_CONNECTION_STRING");
            //var applicationInsightsConnectionString = new ApplicationInsightsConnectionString(applicationInsightsConnectionStringValue);

            //var resourceGroup = await GetResourceGroup(provider, cancellationToken);

            //var benchmarkExecutableUri = await GetBenchmarkExecutableUri(provider, cancellationToken);

            //await VirtualMachineModule.RunOctaneBenchmark(benchmarkScript, benchmarkExecutableUri, diagnosticId, applicationInsightsConnectionString, resourceGroup, virtualMachine, cancellationToken);
        };
    }

    public static DeleteVirtualMachine DeleteVirtualMachine(IServiceProvider provider)
    {
        return async (virtualMachineName, cancellationToken) =>
        {
            var resourceGroup = await GetResourceGroup(provider, cancellationToken);
            await VirtualMachineModule.DeleteVirtualMachine(resourceGroup, virtualMachineName, cancellationToken);
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

    private static async Task<BenchmarkExecutableUri> GetBenchmarkExecutableUri(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        var storageAccountUrl = configuration.GetSection("AzureWebJobsStorage").GetSection("blobServiceUri").Value;
        var artifactsContainerName = configuration.GetNonEmptyValue("STORAGE_ACCOUNT_ARTIFACT_CONTAINER_NAME");
        var benchmarkZipFileName = configuration.GetNonEmptyValue("STORAGE_ACCOUNT_SCRIPT_FILE_NAME");
        var benchmarkExecutableDownloadUri = new Uri(storageAccountUrl).AppendPathSegment(artifactsContainerName)
                                                                       .AppendPathSegment(benchmarkZipFileName)
                                                                       .ToUri();

        var tokenCredential = provider.GetRequiredService<TokenCredential>();
        var blobClient = new BlobClient(benchmarkExecutableDownloadUri, tokenCredential);
        var blobServiceClient = blobClient.GetParentBlobContainerClient().GetParentBlobServiceClient();
        var userDelegationKey = await blobServiceClient.GetUserDelegationKeyAsync(null, DateTimeOffset.UtcNow.AddHours(1), cancellationToken);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            ExpiresOn = userDelegationKey.Value.SignedExpiresOn
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read | BlobSasPermissions.Write);

        var blobUriBuilder = new BlobUriBuilder(blobClient.Uri)
        {
            Sas = sasBuilder.ToSasQueryParameters(userDelegationKey, blobClient.AccountName)
        };

        return new BenchmarkExecutableUri(blobUriBuilder.ToUri().ToString());
    }
}