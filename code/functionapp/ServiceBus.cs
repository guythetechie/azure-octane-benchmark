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

public record VirtualMachineCreationQueueName : NonEmptyString
{
    public VirtualMachineCreationQueueName(string value) : base(value) { }
}

public record VirtualMachineDeletionQueueName : NonEmptyString
{
    public VirtualMachineDeletionQueueName(string value) : base(value) { }
}

public static class ServiceBusModule
{
    public static async ValueTask<Unit> QueueVirtualMachineCreation(ServiceBusClient client, VirtualMachineCreationQueueName queueName, Seq<(VirtualMachine VirtualMachine, DateTimeOffset EnqueueAt)> virtualMachines, CancellationToken cancellationToken)
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

        return Unit.Default;
    }

    public static async ValueTask<Unit> QueueVirtualMachineDeletion(ServiceBusClient client, VirtualMachineDeletionQueueName queueName, VirtualMachineName virtualMachineName, CancellationToken cancellationToken)
    {
        var message = CreateMessage(virtualMachineName);

        var sender = client.CreateSender(queueName);
        await sender.SendMessageAsync(message, cancellationToken);

        return Unit.Default;
    }

    private static ServiceBusMessage CreateMessage(VirtualMachineName virtualMachineName)
    {
        var jsonObject = new JsonObject()
        {
            ["virtualMachineName"] = virtualMachineName.ToString(),
        };

        var messageBytes = JsonSerializer.SerializeToUtf8Bytes(jsonObject);

        return new ServiceBusMessage(messageBytes);
    }

    private static ServiceBusMessage CreateMessage(VirtualMachine virtualMachine, DateTimeOffset enqueueAt)
    {
        var jsonObject = new JsonObject()
        {
            ["virtualMachineName"] = virtualMachine.Name.ToString(),
            ["virtualMachineSku"] = virtualMachine.Sku.ToString()
        };

        var messageBytes = JsonSerializer.SerializeToUtf8Bytes(jsonObject);

        return new ServiceBusMessage(messageBytes)
        {
            ScheduledEnqueueTime = enqueueAt
        };
    }
}
