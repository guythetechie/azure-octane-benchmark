using Azure.Messaging.ServiceBus;
using common;
using LanguageExt;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;

public static class ServiceBusModule
{
    public static async ValueTask QueueVirtualMachineCreation(ServiceBusClient client, VirtualMachineCreationQueueName queueName, Seq<(VirtualMachine VirtualMachine, DateTimeOffset EnqueueAt)> virtualMachines, CancellationToken cancellationToken)
    {
        var sender = client.CreateSender(queueName.Value);
        var messages = virtualMachines.Map(item => CreateMessage(item.VirtualMachine, item.EnqueueAt));
        var queue = new Queue<ServiceBusMessage>(messages);

        while (queue.Count > 0)
        {
            using var batch = await sender.CreateMessageBatchAsync(cancellationToken);

            // Remove messages from queue and add them to batch until the batch is full
            while (queue.Count > 0 && batch.TryAddMessage(queue.Peek()))
            {
                queue.Dequeue();
            }

            if (batch.Count > 0)
            {
                await sender.SendMessagesAsync(batch, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("Could not add message to batch. Payload might be too big.");
            }
        };
    }

    public static async ValueTask QueueOctaneBenchmark(ServiceBusClient client, OctaneBenchmarkQueueName queueName, VirtualMachine virtualMachine, CancellationToken cancellationToken)
    {
        var message = CreateMessage(virtualMachine);

        await SendMessage(client, queueName.Value, message, cancellationToken);
    }

    public static async ValueTask QueueVirtualMachineDeletion(ServiceBusClient client, VirtualMachineDeletionQueueName queueName, VirtualMachineName virtualMachineName, CancellationToken cancellationToken)
    {
        var message = CreateMessage(virtualMachineName);

        await SendMessage(client, queueName.Value, message, cancellationToken);
    }

    private static ServiceBusMessage CreateMessage(VirtualMachine virtualMachine)
    {
        var jsonObject = Serialize(virtualMachine);

        return CreateMessage(jsonObject);
    }

    private static ServiceBusMessage CreateMessage(VirtualMachine virtualMachine, DateTimeOffset enqueueAt)
    {
        var jsonObject = Serialize(virtualMachine);

        return CreateMessage(jsonObject, enqueueAt);
    }

    private static ServiceBusMessage CreateMessage(VirtualMachineName virtualMachineName)
    {
        var jsonObject = Serialize(virtualMachineName);

        return CreateMessage(jsonObject);
    }

    private static JsonObject Serialize(VirtualMachine virtualMachine)
    {
        return new JsonObject()
        {
            ["virtualMachineName"] = virtualMachine.Name.ToString(),
            ["virtualMachineSku"] = virtualMachine.Sku.ToString()
        };
    }

    private static JsonObject Serialize(VirtualMachineName virtualMachineName)
    {
        return new JsonObject()
        {
            ["virtualMachineName"] = virtualMachineName.ToString()
        };
    }

    private static ServiceBusMessage CreateMessage(JsonObject jsonObject)
    {
        var messageBytes = JsonSerializer.SerializeToUtf8Bytes(jsonObject);

        return new ServiceBusMessage(messageBytes);
    }

    private static ServiceBusMessage CreateMessage(JsonObject jsonObject, DateTimeOffset enqueueAt)
    {
        var messageBytes = JsonSerializer.SerializeToUtf8Bytes(jsonObject);

        return new ServiceBusMessage(messageBytes)
        {
            ScheduledEnqueueTime = enqueueAt
        };
    }

    private static async ValueTask SendMessage(ServiceBusClient client, string queueName, ServiceBusMessage message, CancellationToken cancellationToken)
    {
        var sender = client.CreateSender(queueName);

        await sender.SendMessageAsync(message, cancellationToken);
    }
}
