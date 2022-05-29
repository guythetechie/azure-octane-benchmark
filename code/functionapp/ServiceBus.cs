using Azure.Messaging.ServiceBus;
using common;
using LanguageExt;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using static LanguageExt.Prelude;

namespace functionapp;

public record VirtualMachineCreationQueueName : NonEmptyString
{
    public VirtualMachineCreationQueueName(string value) : base(value) { }
}

public record OctaneBenchmarkQueueName : NonEmptyString
{
    public OctaneBenchmarkQueueName(string value) : base(value) { }
}

public record VirtualMachineDeletionQueueName : NonEmptyString
{
    public VirtualMachineDeletionQueueName(string value) : base(value) { }
}

public static class ServiceBusModule
{
    public static Aff<Unit> QueueVirtualMachineCreation(ServiceBusClient client, VirtualMachineCreationQueueName queueName, Seq<(VirtualMachine VirtualMachine, DateTimeOffset EnqueueAt)> virtualMachines, CancellationToken cancellationToken)
    {
        return Aff(async () =>
        {
            var sender = client.CreateSender(queueName);
            var messages = virtualMachines.Map(item => CreateMessage(item.VirtualMachine, item.EnqueueAt));
            var queue = new Queue<ServiceBusMessage>(messages);

            while (queue.Count > 0)
            {
                using var batch = await sender.CreateMessageBatchAsync(cancellationToken);

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
            }

            return unit;
        });
    }

    public static Aff<Unit> QueueOctaneBenchmark(ServiceBusClient client, OctaneBenchmarkQueueName queueName, VirtualMachine virtualMachine, CancellationToken cancellationToken)
    {
        return Aff(async () =>
        {
            var message = CreateMessage(virtualMachine);

            return await SendMessage(client, queueName, message, cancellationToken);
        });
    }

    public static Aff<Unit> QueueVirtualMachineDeletion(ServiceBusClient client, VirtualMachineDeletionQueueName queueName, VirtualMachineName virtualMachineName, CancellationToken cancellationToken)
    {
        return Aff(async () =>
        {
            var message = CreateMessage(virtualMachineName);

            return await SendMessage(client, queueName, message, cancellationToken);
        });
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

    private static async ValueTask<Unit> SendMessage(ServiceBusClient client, string queueName, ServiceBusMessage message, CancellationToken cancellationToken)
    {
        var sender = client.CreateSender(queueName);

        await sender.SendMessageAsync(message, cancellationToken);

        return unit;
    }
}
