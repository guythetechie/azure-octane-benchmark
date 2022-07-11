using Azure.Messaging.ServiceBus;
using common;
using LanguageExt;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;

public class Benchmarker
{
    private readonly RunOctaneBenchmark runOctaneBenchmark;
    private readonly QueueVirtualMachineDeletion queueVirtualMachineDeletion;

    public Benchmarker(RunOctaneBenchmark runOctaneBenchmark, QueueVirtualMachineDeletion queueVirtualMachineDeletion)
    {
        this.runOctaneBenchmark = runOctaneBenchmark;
        this.queueVirtualMachineDeletion = queueVirtualMachineDeletion;
    }

    [FunctionName("benchmarker")]
    public async Task Run([ServiceBusTrigger("%SERVICE_BUS_RUN_OCTANE_BENCHMARK_QUEUE_NAME%", Connection = "ServiceBusConnection", AutoCompleteMessages = false)] ServiceBusReceivedMessage message, ServiceBusMessageActions messageActions, ILogger logger, CancellationToken cancellationToken)
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
            var virtualMachine = GetVirtualMachine(requestJson);

            logger.LogInformation("Getting diagnostic ID from service bus message...");
            var diagnosticId = GetDiagnosticId(message);

            logger.LogInformation("Running benchmark...");
            await runOctaneBenchmark(logger, virtualMachine, diagnosticId, linkedToken);

            logger.LogInformation("Queuing virtual machine for deletion...");
            await queueVirtualMachineDeletion(virtualMachine.Name, linkedToken);

            logger.LogInformation("Completing service bus message...");
            await messageActions.CompleteMessageAsync(message, linkedToken);
        }
        catch (JsonException jsonException)
        {
            logger.LogCritical($"JSON is invalid. Error is '{jsonException.Message}'. Sending message to dead-letter queue...");
            await messageActions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidJson", deadLetterErrorDescription: jsonException.Message, cancellationToken);
            throw;
        }
        catch (InvalidOperationException exception)
        {
            logger.LogCritical($"Invalid operation. Error is '{exception.Message}'. Sending message to dead-letter queue...");
            await messageActions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidOperation", deadLetterErrorDescription: exception.Message, cancellationToken);
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

    private static VirtualMachine GetVirtualMachine(JsonObject jsonObject)
    {
        var virtualMachineNameString = jsonObject.GetNonEmptyStringProperty("virtualMachineName");
        var virtualMachineName = new VirtualMachineName(virtualMachineNameString);

        var virtualMachineSkuString = jsonObject.GetNonEmptyStringProperty("virtualMachineSku");
        var virtualMachineSku = new VirtualMachineSku(virtualMachineSkuString);

        return new VirtualMachine(virtualMachineName, virtualMachineSku);
    }

    private static DiagnosticId GetDiagnosticId(ServiceBusReceivedMessage message)
    {
        var diagnosticIdObject = message.ApplicationProperties.TryGetValue("Diagnostic-Id")
                                                              .IfNoneThrow("Could not find property 'Diagnostic-Id' in service bus message.");

        var diagnosticIdString = diagnosticIdObject.ToString();

        return string.IsNullOrWhiteSpace(diagnosticIdString)
                ? throw new InvalidOperationException("Service bus message property 'Diagnostic-Id' has a null or whitespace value.")
                : new DiagnosticId(diagnosticIdString);
    }
}
