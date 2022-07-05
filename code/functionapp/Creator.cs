using Azure.Messaging.ServiceBus;
using common;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;

public class Creator
{
    private readonly CreateVirtualMachine createVirtualMachine;
    private readonly QueueOctaneBenchmark queueOctaneBenchmark;

    public Creator(CreateVirtualMachine createVirtualMachine, QueueOctaneBenchmark queueOctaneBenchmark)
    {
        this.createVirtualMachine = createVirtualMachine;
        this.queueOctaneBenchmark = queueOctaneBenchmark;
    }

    [FunctionName("create-virtual-machine")]
    public async Task Run([ServiceBusTrigger("%SERVICE_BUS_CREATE_VM_QUEUE_NAME%", Connection = "ServiceBusConnection", AutoCompleteMessages = false)] ServiceBusReceivedMessage message, ServiceBusMessageActions messageActions, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            // ServiceBus gives 5 minutes to process a message before considering it abandoned. We timeout
            // after 4 minutes and explicitly abandon the message.
            logger.LogInformation("Setting up timeout...");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(4));
            var linkedToken = cts.Token;

            logger.LogInformation("Getting JSON from request...");
            var requestJson = JsonObjectModule.FromBinaryData(message.Body);
            logger.LogInformation("Received payload {CreateVirtualMachineRequestJson}", requestJson.SerializeToString());

            logger.LogInformation("Converting payload to virtual machine...");
            var virtualMachine = DeserializeToVirtualMachine(requestJson);

            logger.LogInformation("Creating virtual machine...");
            await createVirtualMachine(virtualMachine, linkedToken);

            logger.LogInformation("Queuing virtual machine for Octane benchmark...");
            await queueOctaneBenchmark(virtualMachine, linkedToken);

            logger.LogInformation("Completing service bus message...");
            await messageActions.CompleteMessageAsync(message, linkedToken);
        }
        catch (JsonException jsonException)
        {
            logger.LogCritical($"JSON is invalid. Error is '{jsonException.Message}'. Sending message to dead-letter queue...");
            await messageActions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidJson", deadLetterErrorDescription: jsonException.Message, cancellationToken);
            throw;
        }
        catch (Azure.RequestFailedException requestFailedException) when (requestFailedException.Status == 400)
        {
            logger.LogCritical($"Azure request failed. Error is '{requestFailedException.Message}'. Sending message to dead-letter queue...");
            await messageActions.DeadLetterMessageAsync(message, deadLetterReason: requestFailedException.ErrorCode, deadLetterErrorDescription: requestFailedException.Message, cancellationToken);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Throw if it's a non-timeout cancellation, otherwise abandon message.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            else
            {
                logger.LogWarning("Function app timed out, abandoning message...");
                await messageActions.AbandonMessageAsync(message, cancellationToken: cancellationToken);
            }
        }
    }

    private static VirtualMachine DeserializeToVirtualMachine(JsonObject jsonObject)
    {
        var virtualMachineNameString = jsonObject.GetNonEmptyStringProperty("virtualMachineName");
        var virtualMachineName = new VirtualMachineName(virtualMachineNameString);

        var virtualMachineSkuString = jsonObject.GetNonEmptyStringProperty("virtualMachineSku");
        var virtualMachineSku = new VirtualMachineSku(virtualMachineSkuString);

        return new VirtualMachine(virtualMachineName, virtualMachineSku);
    }
}
