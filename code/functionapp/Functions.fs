namespace functionapp

open Azure.Messaging.ServiceBus
open FSharpPlus
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Extensions.Http
open Microsoft.Extensions.Logging
open System
open System.IO
open System.Text.Json.Nodes
open System.Threading

open common

type CreateVirtualMachineQueueName =
    private
    | CreateVirtualMachineQueueName of ServiceBusQueueName

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Create virtual machine queue name cannot be empty."
        else
            value
            |> ServiceBusQueueName.fromString
            |> CreateVirtualMachineQueueName

    static member toServiceBusQueueName(CreateVirtualMachineQueueName serviceBusQueueName) = serviceBusQueueName

type RunBenchmarkQueueName =
    private
    | RunBenchmarkQueueName of ServiceBusQueueName

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Create virtual machine queue name cannot be empty."
        else
            value
            |> ServiceBusQueueName.fromString
            |> RunBenchmarkQueueName

    static member toServiceBusQueueName(RunBenchmarkQueueName serviceBusQueueName) = serviceBusQueueName

type DeleteVirtualMachineQueueName =
    private
    | DeleteVirtualMachineQueueName of ServiceBusQueueName

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Create virtual machine queue name cannot be empty."
        else
            value
            |> ServiceBusQueueName.fromString
            |> DeleteVirtualMachineQueueName

    static member toServiceBusQueueName(DeleteVirtualMachineQueueName serviceBusQueueName) = serviceBusQueueName

type ScriptContent =
    private
    | ScriptContent of byte array

    static member fromBase64String value =
        value |> Convert.FromBase64String |> ScriptContent

    static member toBytes(ScriptContent bytes) = bytes

type CustomScriptUri =
    private
    | CustomScriptUri of string

    static member fromString value =
        match Uri.TryCreate(value, UriKind.Absolute) with
        | true, uri -> uri.ToString() |> CustomScriptUri
        | _ -> invalidOp $"'{value}' is not a valid absolute URL."

    static member toUri(CustomScriptUri value) = new Uri(value)

[<RequireQualifiedAccess>]
module Functions =
    let private deadLetterJsonError messageActions message jsonError =
        async {
            let properties =
                Map.empty
                |> Map.add "DeadLetterReason" "InvalidJson"
                |> Map.add "DeadLetterErrorDescription" (JsonError.toString jsonError)
                |> Map.mapValues box

            do! ServiceBusMessageActions.deadLetterMessage messageActions properties message

            return
                jsonError
                |> JsonError.toString
                |> invalidOp
                |> raise
        }

    let private deadLetterRequestFailedException messageActions message (error: exn) =
        async {
            match error with
            | :? Azure.RequestFailedException as requestFailed ->
                match requestFailed.Status with
                | 400 ->
                    let properties =
                        Map.empty
                        |> Map.add "DeadLetterReason" "AzureRequestFailedException"
                        |> Map.add "DeadLetterErrorDescription" requestFailed.Message
                        |> Map.mapValues box

                    do! ServiceBusMessageActions.deadLetterMessage messageActions properties message

                    return raise error
                | _ -> return raise error
            | _ -> return raise error
        }

    let private tryDeserializeVirtualMachine (jsonObject: JsonObject) =
        resultCE {
            let! nameString = JsonObject.tryGetNonEmptyStringProperty "virtualMachineName" jsonObject
            and! skuString = JsonObject.tryGetNonEmptyStringProperty "virtualMachineSku" jsonObject

            return
                { VirtualMachine.Name = VirtualMachineName.fromNonEmptyString nameString
                  Sku = VirtualMachineSku.fromNonEmptyString skuString }
        }

    let private serializeVirtualMachine (virtualMachine: VirtualMachine) =
        new JsonObject()
        |> JsonObject.addStringProperty "virtualMachineName" (VirtualMachineName.toString virtualMachine.Name)
        |> JsonObject.addStringProperty "virtualMachineSku" (VirtualMachineSku.toString virtualMachine.Sku)

    let createJob client createVirtualMachineQueueName (logger: ILogger) (request: HttpRequest) =
        let errorToActionResult error =
            let jsonObject =
                new JsonObject()
                |> JsonObject.addStringProperty "code" "InvalidJson"
                |> JsonObject.addStringProperty "message" (JsonError.toString error)

            BadRequestObjectResult(JsonObject.toString jsonObject) :> IActionResult

        let generateEnqueueTime index =
            DateTimeOffset.UtcNow.AddSeconds(Random.Shared.NextDouble() * 10.0 * (float index))

        let generateVirtualMachineName () =
            let suffix =
                Path
                    .GetRandomFileName()
                    .Replace(".", "", StringComparison.OrdinalIgnoreCase)
                |> Seq.take 5
                |> String.Concat

            VirtualMachineName.fromString $"octane-{suffix}"

        let deserializeVirtualMachine jsonObject =
            resultCE {
                let! sku =
                    jsonObject
                    |> JsonObject.tryGetNonEmptyStringProperty "sku"
                    |> Result.map VirtualMachineSku.fromNonEmptyString

                let! count =
                    jsonObject
                    |> JsonObject.tryGetUIntProperty "count"

                return (sku, count)
            }

        asyncResultCE {
            logger.LogInformation("Deserializing request body...")
            let! jsonObject = JsonObject.tryFromStream request.Body
            logger.LogInformation("Request payload: {CreateJobRequestJson}", JsonObject.toString jsonObject)

            let! messages =
                jsonObject
                |> JsonObject.tryGetJsonObjectArrayProperty "virtualMachines"
                |> Result.bind (Result.traverseSeq deserializeVirtualMachine)
                |> Result.map (
                    List.collect (fun (sku, count) -> List.replicate (int count) sku)
                    >> List.map (
                        (fun sku ->
                            { VirtualMachine.Name = generateVirtualMachineName ()
                              Sku = sku })
                        >> serializeVirtualMachine
                        >> ServiceBusMessage.fromJsonObject
                    )
                    >> List.mapi (fun index message ->
                        message.ScheduledEnqueueTime <- generateEnqueueTime index
                        message)
                )
                |> AsyncResult.fromResult

            logger.LogInformation("Total virtual machines requested: {VirtualMachineCount}", messages.Length)

            logger.LogInformation("Queuing virtual machines for creation...")

            do!
                ServiceBusClient.batchSendMessages
                    client
                    (CreateVirtualMachineQueueName.toServiceBusQueueName createVirtualMachineQueueName)
                    messages
                |> AsyncResult.fromAsync

            return new NoContentResult() :> IActionResult
        }
        |> AsyncResult.defaultWith errorToActionResult

    let createVirtualMachine
        client
        runBenchmarkQueueName
        (logger: ILogger)
        resourceGroup
        subnetId
        messageActions
        (message: ServiceBusReceivedMessage)
        =
        asyncResultCE {
            logger.LogInformation("Deserializing service bus message...")

            let! virtualMachine =
                message.Body.ToArray()
                |> JsonObject.tryFromBytes
                |> Result.bind tryDeserializeVirtualMachine
                |> AsyncResult.fromResult

            logger.LogInformation(
                "Creating virtual machine {VirtualMachineName} with SKU {VirtualMachineSku}...",
                VirtualMachineName.toString virtualMachine.Name,
                VirtualMachineSku.toString virtualMachine.Sku
            )

            do!
                VirtualMachine.create resourceGroup subnetId virtualMachine
                |> AsyncResult.fromAsync

            logger.LogInformation("Queuing virtual machine for benchmark...")

            do!
                virtualMachine
                |> serializeVirtualMachine
                |> ServiceBusMessage.fromJsonObject
                |> ServiceBusClient.sendMessage
                    client
                    (RunBenchmarkQueueName.toServiceBusQueueName runBenchmarkQueueName)
                |> AsyncResult.fromAsync

            logger.LogInformation("Completing message...")

            do!
                ServiceBusMessageActions.completeMessage messageActions message
                |> AsyncResult.fromAsync
        }
        |> AsyncResult.defaultWithAsync (deadLetterJsonError messageActions message)
        |> Async.catch
        |> AsyncResult.defaultWithAsync (deadLetterRequestFailedException messageActions message)

    let runBenchmark
        client
        deleteVirtualMachineQueueName
        artifactsContainerClient
        (logger: ILogger)
        resourceGroup
        applicationInsightsConnectionString
        messageActions
        (message: ServiceBusReceivedMessage)
        =
        asyncResultCE {
            logger.LogInformation("Deserializing service bus message...")

            let! virtualMachine =
                message.Body.ToArray()
                |> JsonObject.tryFromBytes
                |> Result.bind tryDeserializeVirtualMachine
                |> AsyncResult.fromResult

            logger.LogInformation(
                "Running benchmark on virtual machine {VirtualMachineName} with SKU {VirtualMachineSku}...",
                VirtualMachineName.toString virtualMachine.Name,
                VirtualMachineSku.toString virtualMachine.Sku
            )

            let! scriptUri =
                BlobContainerClient.getAuthenticatedBlobUri
                    artifactsContainerClient
                    (DateTimeOffset.UtcNow.AddHours(1.0))
                    "Run-OctaneBenchmark.ps1"
                |> AsyncResult.fromAsync

            let! scriptParameters =
                async {
                    let! benchmarkDownloadUri =
                        BlobContainerClient.getAuthenticatedBlobUri
                            artifactsContainerClient
                            (DateTimeOffset.UtcNow.AddHours(1.0))
                            "benchmark.zip"

                    let diagnosticId =
                        message.ApplicationProperties.["Diagnostic-Id"]
                        |> string

                    return
                        Map.empty
                        |> Map.add "BenchmarkDownloadUri" (string benchmarkDownloadUri)
                        |> Map.add "DiagnosticId" diagnosticId
                        |> Map.add "VirtualMachineSku" (VirtualMachineSku.toString virtualMachine.Sku)
                        |> Map.add
                            "ApplicationInsightsConnectionString"
                            (ApplicationInsightsConnectionString.toString applicationInsightsConnectionString)
                }
                |> AsyncResult.fromAsync

            do!
                VirtualMachine.runCustomScript scriptUri scriptParameters resourceGroup
                |> AsyncResult.fromAsync

            logger.LogInformation("Queuing virtual machine for deletion...")

            do!
                virtualMachine
                |> serializeVirtualMachine
                |> ServiceBusMessage.fromJsonObject
                |> ServiceBusClient.sendMessage
                    client
                    (DeleteVirtualMachineQueueName.toServiceBusQueueName deleteVirtualMachineQueueName)
                |> AsyncResult.fromAsync

            logger.LogInformation("Completing message...")

            do!
                ServiceBusMessageActions.completeMessage messageActions message
                |> AsyncResult.fromAsync
        }
        |> AsyncResult.defaultWithAsync (deadLetterJsonError messageActions message)
        |> Async.catch
        |> AsyncResult.defaultWithAsync (deadLetterRequestFailedException messageActions message)

    let deleteVirtualMachine (logger: ILogger) resourceGroup messageActions (message: ServiceBusReceivedMessage) =
        asyncResultCE {
            logger.LogInformation("Deserializing service bus message...")

            let! virtualMachine =
                message.Body.ToArray()
                |> JsonObject.tryFromBytes
                |> Result.bind tryDeserializeVirtualMachine
                |> AsyncResult.fromResult

            logger.LogInformation(
                "Deleting virtual machine {VirtualMachineName} with SKU {VirtualMachineSku}...",
                VirtualMachineName.toString virtualMachine.Name,
                VirtualMachineSku.toString virtualMachine.Sku
            )

            do!
                VirtualMachine.deleteByName resourceGroup virtualMachine.Name
                |> AsyncResult.fromAsync

            logger.LogInformation("Completing message...")

            do!
                ServiceBusMessageActions.completeMessage messageActions message
                |> AsyncResult.fromAsync
        }
        |> AsyncResult.defaultWithAsync (deadLetterJsonError messageActions message)
        |> Async.catch
        |> AsyncResult.defaultWithAsync (deadLetterRequestFailedException messageActions message)

type CreateJobFunction(client, createVirtualMachineQueueName) =
    [<FunctionName("create-job")>]
    member this.Run
        (
            [<HttpTrigger(AuthorizationLevel.Function, "post", Route = null)>] request: HttpRequest,
            logger,
            cancellationToken: CancellationToken
        ) =
        Functions.createJob client createVirtualMachineQueueName logger request
        |> Async.toTask cancellationToken

type CreateVirtualMachineFunction(client, resourceGroup, runBenchmarkQueueName, subnetId) =
    [<FunctionName("create-virtual-machine")>]
    member this.Run
        (
            [<ServiceBusTrigger("%SERVICE_BUS_CREATE_VM_QUEUE_NAME%",
                                Connection = "ServiceBusConnection",
                                AutoCompleteMessages = false)>] message,
            messageActions,
            logger,
            cancellationToken
        ) =
        Functions.createVirtualMachine client runBenchmarkQueueName logger resourceGroup subnetId messageActions message
        |> Async.toTask cancellationToken

type RunBenchmark
    (
        client,
        artifactsContainerClient,
        deleteVirtualMachineQueueName,
        resourceGroup,
        applicationInsightsConnectionString
    ) =
    [<FunctionName("run-benchmark")>]
    member this.Run
        (
            [<ServiceBusTrigger("%SERVICE_BUS_RUN_BENCHMARK_QUEUE_NAME%",
                                Connection = "ServiceBusConnection",
                                AutoCompleteMessages = false)>] message,
            messageActions,
            logger,
            cancellationToken
        ) =
        Functions.runBenchmark
            client
            deleteVirtualMachineQueueName
            artifactsContainerClient
            logger
            resourceGroup
            applicationInsightsConnectionString
            messageActions
            message
        |> Async.toTask cancellationToken

type DeleteVirtualMachineFunction(resourceGroup) =
    [<FunctionName("delete-virtual-machine")>]
    member this.Run
        (
            [<ServiceBusTrigger("%SERVICE_BUS_DELETE_VM_QUEUE_NAME%",
                                Connection = "ServiceBusConnection",
                                AutoCompleteMessages = false)>] message,
            messageActions,
            logger,
            cancellationToken
        ) =
        Functions.deleteVirtualMachine logger resourceGroup messageActions message
        |> Async.toTask cancellationToken
