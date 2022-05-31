namespace functionapp

open Azure.Core
open Azure.Identity
open Azure.Messaging.ServiceBus
open Azure.ResourceManager
open Azure.ResourceManager.Resources
open Azure.Storage.Blobs
open FSharpPlus
open Microsoft.Azure.Functions.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open System

open common

[<RequireQualifiedAccess>]
module ServiceProvider =
    let getTokenCredential (provider: IServiceProvider) =
        let credentialOptions = new DefaultAzureCredentialOptions()

        credentialOptions.AuthorityHost <-
            provider.GetRequiredService<IConfiguration>()
            |> Configuration.getOptionalValue "AZURE_ENVIRONMENT"
            |> Option.map (function
                | nameof (AzureAuthorityHosts.AzureChina) -> AzureAuthorityHosts.AzureChina
                | nameof (AzureAuthorityHosts.AzurePublicCloud) -> AzureAuthorityHosts.AzurePublicCloud
                | nameof (AzureAuthorityHosts.AzureGermany) -> AzureAuthorityHosts.AzureGermany
                | nameof (AzureAuthorityHosts.AzureGovernment) -> AzureAuthorityHosts.AzureGovernment
                | authority -> invalidOp $"'{authority}' is not a valid Azure environment.")
            |> Option.defaultValue AzureAuthorityHosts.AzurePublicCloud

        DefaultAzureCredential(credentialOptions) :> TokenCredential

    let getArmClient (provider: IServiceProvider) =
        provider.GetRequiredService<TokenCredential>()
        |> ArmClient

    let getResourceGroup (provider: IServiceProvider) =
        async {
            let armClient = provider.GetRequiredService<ArmClient>()
            let! cancellationToken = Async.CancellationToken

            let! subscription =
                armClient.GetDefaultSubscriptionAsync(cancellationToken)
                |> Async.AwaitTask

            let resourceGroupName =
                provider.GetRequiredService<IConfiguration>()
                |> Configuration.getValue "VIRTUAL_MACHINE_RESOURCE_GROUP_NAME"

            return!
                subscription.GetResourceGroupAsync(resourceGroupName, cancellationToken)
                |> Async.AwaitTask
                |> Async.map (fun x -> x.Value)
        }
        |> Async.RunSynchronously

    let getSubnetId (provider: IServiceProvider) =
        provider.GetRequiredService<IConfiguration>()
        |> Configuration.getValue "VIRTUAL_MACHINE_SUBNET_ID"
        |> SubnetId.fromString

    let getArtifactsContainerClient (provider: IServiceProvider) =
        let configuration =
            provider.GetRequiredService<IConfiguration>()

        let storageAccountUrl =
            configuration
            |> Configuration.getSection "AzureWebJobsStorage"
            |> Configuration.getValue "blobServiceUri"
            |> Uri

        let credential =
            provider.GetRequiredService<TokenCredential>()

        let containerName =
            configuration
            |> Configuration.getValue "STORAGE_ACCOUNT_ARTIFACT_CONTAINER_NAME"

        BlobServiceClient(storageAccountUrl, credential)
            .GetBlobContainerClient(containerName)

    let getServiceBusClient (provider: IServiceProvider) =
        let getFromTokenCredential configuration =
            let credential =
                provider.GetRequiredService<TokenCredential>()

            configuration
            |> Configuration.getSection "ServiceBusConnection"
            |> Configuration.getValue "fullyQualifiedNamespace"
            |> fun serviceBusNamespace -> ServiceBusClient(serviceBusNamespace, credential)

        let configuration =
            provider.GetRequiredService<IConfiguration>()

        configuration
        |> Configuration.getOptionalValue "SERVICE_BUS_CONNECTION_STRING"
        |> Option.map ServiceBusClient
        |> Option.defaultWith (fun () -> getFromTokenCredential configuration)

    let getCreateVirtualMachineQueueName (provider: IServiceProvider) =
        provider.GetRequiredService<IConfiguration>()
        |> Configuration.getValue "SERVICE_BUS_CREATE_VM_QUEUE_NAME"
        |> CreateVirtualMachineQueueName.fromString

    let getRunBenchmarkQueueName (provider: IServiceProvider) =
        provider.GetRequiredService<IConfiguration>()
        |> Configuration.getValue "SERVICE_BUS_RUN_BENCHMARK_QUEUE_NAME"
        |> RunBenchmarkQueueName.fromString

    let getDeleteVirtualMachineQueueName (provider: IServiceProvider) =
        provider.GetRequiredService<IConfiguration>()
        |> Configuration.getValue "SERVICE_BUS_DELETE_VM_QUEUE_NAME"
        |> DeleteVirtualMachineQueueName.fromString

    let getApplicationInsightsConnectionString (provider: IServiceProvider) =
        provider.GetRequiredService<IConfiguration>()
        |> Configuration.getValue "APPLICATIONINSIGHTS_CONNECTION_STRING"
        |> ApplicationInsightsConnectionString.fromString

[<RequireQualifiedAccess>]
module ServiceCollection =
    let configure (services: IServiceCollection) =
        services
            .AddSingleton<TokenCredential>(ServiceProvider.getTokenCredential)
            .AddSingleton<ArmClient>(ServiceProvider.getArmClient)
            .AddSingleton<ResourceGroupResource>(ServiceProvider.getResourceGroup)
            .AddSingleton<SubnetId>(ServiceProvider.getSubnetId)
            .AddSingleton<BlobContainerClient>(ServiceProvider.getArtifactsContainerClient)
            .AddSingleton<ServiceBusClient>(ServiceProvider.getServiceBusClient)
            .AddSingleton<CreateVirtualMachineQueueName>(ServiceProvider.getCreateVirtualMachineQueueName)
            .AddSingleton<RunBenchmarkQueueName>(ServiceProvider.getRunBenchmarkQueueName)
            .AddSingleton<DeleteVirtualMachineQueueName>(ServiceProvider.getDeleteVirtualMachineQueueName)
            .AddSingleton<ApplicationInsightsConnectionString>(ServiceProvider.getApplicationInsightsConnectionString)
        |> ignore

type Startup() =
    inherit FunctionsStartup()

    override this.Configure(builder: IFunctionsHostBuilder) =
        ServiceCollection.configure builder.Services

[<assembly: FunctionsStartup(typeof<Startup>)>]
do ()
