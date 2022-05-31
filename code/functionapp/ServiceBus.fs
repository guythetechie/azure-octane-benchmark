namespace functionapp

open Azure.Messaging.ServiceBus
open FSharp.Control
open Microsoft.Azure.WebJobs.ServiceBus
open System
open System.Text.Json.Nodes

open common

type ServiceBusQueueName =
    private
    | ServiceBusQueueName of NonEmptyString

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Virtual machine SKU cannot be empty."
        else
            value
            |> NonEmptyString.fromString
            |> ServiceBusQueueName

    static member toString(ServiceBusQueueName nonEmptyString) = NonEmptyString.toString nonEmptyString

[<RequireQualifiedAccess>]
module ServiceBusMessage =
    let fromJsonObject (jsonObject: JsonObject) =
        jsonObject
        |> JsonObject.toBytes
        |> ServiceBusMessage

    let scheduleFromJsonObject jsonObject enqueueAt =
        let message = fromJsonObject jsonObject
        message.ScheduledEnqueueTime <- enqueueAt
        message

[<RequireQualifiedAccess>]
module ServiceBusClient =
    let sendMessage (client: ServiceBusClient) queueName message =
        async {
            let sender =
                client.CreateSender(ServiceBusQueueName.toString queueName)

            let! cancellationToken = Async.CancellationToken

            do!
                sender.SendMessageAsync(message, cancellationToken)
                |> Async.AwaitTask
        }

    let batchSendMessages (client: ServiceBusClient) queueName messages =
        let sendBatch (sender: ServiceBusSender) (batch: ServiceBusMessageBatch) =
            async {
                let! cancellationToken = Async.CancellationToken

                do!
                    sender.SendMessagesAsync(batch, cancellationToken)
                    |> Async.AwaitTask
            }

        let createBatch (sender: ServiceBusSender) =
            async {
                let! cancellationToken = Async.CancellationToken

                return!
                    sender.CreateMessageBatchAsync(cancellationToken)
                    |> Async.fromValueTaskOf
            }

        let rec folder (sender: ServiceBusSender) (batch: ServiceBusMessageBatch) message =
            async {
                if batch.TryAddMessage(message) then
                    return batch
                else if batch.Count > 0 then
                    do! sendBatch sender batch

                    let! newBatch = createBatch sender
                    return! folder sender newBatch message
                else
                    return invalidOp "Could not add message to batch. Message might be too big."
            }

        async {
            let sender =
                client.CreateSender(ServiceBusQueueName.toString queueName)

            let! originalBatch = createBatch sender

            let! finalBatch =
                messages
                |> AsyncSeq.ofSeq
                |> AsyncSeq.foldAsync (folder sender) originalBatch

            do! sendBatch sender finalBatch
        }

[<RequireQualifiedAccess>]
module ServiceBusMessageActions =
    let completeMessage (actions: ServiceBusMessageActions) message =
        async {
            let! cancellationToken = Async.CancellationToken

            do!
                actions.CompleteMessageAsync(message, cancellationToken)
                |> Async.AwaitTask
        }

    let abandonMessage (actions: ServiceBusMessageActions) (properties: Map<string, obj>) message =
        async {
            let! cancellationToken = Async.CancellationToken

            do!
                actions.AbandonMessageAsync(message, properties, cancellationToken)
                |> Async.AwaitTask
        }

    let deadLetterMessage (actions: ServiceBusMessageActions) (properties: Map<string, obj>) message =
        async {
            let! cancellationToken = Async.CancellationToken

            do!
                actions.DeadLetterMessageAsync(message, properties, cancellationToken)
                |> Async.AwaitTask
        }
