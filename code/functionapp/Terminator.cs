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

public class Terminator
{
    private readonly DeleteVirtualMachine deleteVirtualMachine;

    public Terminator(DeleteVirtualMachine deleteVirtualMachine)
    {
        this.deleteVirtualMachine = deleteVirtualMachine;
    }

    [FunctionName("delete-virtual-machine")]
    public async Task Run([ServiceBusTrigger("%SERVICE_BUS_DELETE_VM_QUEUE_NAME%", Connection = "ServiceBusConnection", AutoCompleteMessages = false)] ServiceBusReceivedMessage message, ServiceBusMessageActions messageActions, ILogger logger, CancellationToken cancellationToken)
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
            logger.LogInformation("Received payload {DeleteVirtualMachineRequestJson}", requestJson.SerializeToString());

            logger.LogInformation("Converting payload to virtual machine...");
            var virtualMachineName = GetVirtualMachineName(requestJson);

            logger.LogInformation("Deleting virtual machine...");
            await deleteVirtualMachine(virtualMachineName, linkedToken);

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

    private static VirtualMachineName GetVirtualMachineName(JsonObject jsonObject)
    {
        var virtualMachineNameString = jsonObject.GetNonEmptyStringProperty("virtualMachineName");
        return new VirtualMachineName(virtualMachineNameString);
    }
}
