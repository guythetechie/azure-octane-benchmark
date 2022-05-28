using Azure;
using Azure.Messaging.ServiceBus;
using common;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using static LanguageExt.Prelude;

namespace functionapp;

public class VirtualMachineBenchmarker
{
    private readonly RunOctaneBenchmark runOctaneBenchmark;
    private readonly QueueVirtualMachineDeletion queueVirtualMachineDeletion;

    public VirtualMachineBenchmarker(RunOctaneBenchmark runOctaneBenchmark, QueueVirtualMachineDeletion queueVirtualMachineDeletion)
    {
        this.runOctaneBenchmark = runOctaneBenchmark;
        this.queueVirtualMachineDeletion = queueVirtualMachineDeletion;
    }

    [FunctionName("run-octane-benchmark")]
    public async Task Run([ServiceBusTrigger("%SERVICE_BUS_RUN_OCTANE_BENCHMARK_QUEUE_NAME%", Connection = "ServiceBusConnection", AutoCompleteMessages = false)] ServiceBusReceivedMessage message, ServiceBusMessageActions messageActions, ILogger logger, CancellationToken cancellationToken)
    {
        var tryGetJsonObject = () => JsonObjectModule.FromBinaryData(message.Body);

        var tryGetDiagnosticId = () => message.ApplicationProperties.TryGetValue("Diagnostic-Id")
                                                                    .Map(value => value.ToString())
                                                                    .Filter(value => string.IsNullOrWhiteSpace(value) is false)
                                                                    .Map(value => new DiagnosticId(value!))
                                                                    .ToEff(Error.New("Diagnostic ID does not exist or is empty."));

        var completeMessage = () => messageActions.CompleteMessageAsync(message, cancellationToken)
                                                  .ToUnitValueTask();

        var handleTimeout = Aff(() =>
        {
            logger.LogWarning("Function app timed out, abandoning message...");

            return messageActions.AbandonMessageAsync(message, cancellationToken: cancellationToken)
                                 .ToUnitValueTask();
        });

        var handleInvalidJson = (Error error) => Aff(() =>
        {
            logger.LogCritical($"JSON is invalid. Error is '{error.Message}'. Sending message to dead-letter queue...");

            return messageActions.DeadLetterMessageAsync(message, deadLetterReason: nameof(CommonErrorCode.InvalidJson), deadLetterErrorDescription: error.Message, cancellationToken)
                                 .ToUnitValueTask();
        });

        var handleRequestFailedException = (Exception exception) => Aff(() =>
        {
            var requestFailedException = (RequestFailedException)exception;
            logger.LogCritical($"Azure request failed. Error is '{requestFailedException.Message}'. Sending message to dead-letter queue...");

            return messageActions.DeadLetterMessageAsync(message,
                                                        deadLetterReason: requestFailedException.ErrorCode,
                                                        deadLetterErrorDescription: requestFailedException.Message,
                                                        cancellationToken)
                                 .ToUnitValueTask();
        });

        await tryGetJsonObject().Do(jsonObject => logger.LogInformation("Request payload: {RunOctaneBenchmarkRequestJson}", jsonObject.SerializeToString()))
                                .Bind(GetVirtualMachine)
                                .Do(_ => logger.LogInformation("Getting diagnostic ID..."))
                                .Bind(virtualMachine => tryGetDiagnosticId().Map(diagnosticId => (virtualMachine, diagnosticId)))
                                .Do(_ => logger.LogInformation("Running Octane benchmark..."))
                                .Do(tuple => runOctaneBenchmark(tuple.virtualMachine, tuple.diagnosticId, cancellationToken))
                                .Do(_ => logger.LogInformation("Queueing virtual machine for deletion..."))
                                .Do(tuple => queueVirtualMachineDeletion(tuple.virtualMachine.Name, cancellationToken))
                                .Do(_ => logger.LogInformation("Completing service bus message.."))
                                .Iter(_ => completeMessage())
                                .Timeout(TimeSpan.FromMinutes(4.5))
                                .Catch(Errors.TimedOut, handleTimeout)
                                .Catch(CommonErrorCode.InvalidJson, handleInvalidJson)
                                .Catch(exception => exception is RequestFailedException requestFailedException && (requestFailedException.Status == 400), handleRequestFailedException)
                                .RunAndThrowIfFail();
    }

    private static Eff<VirtualMachine> GetVirtualMachine(JsonObject jsonObject)
    {
        return from virtualMachineNameString in jsonObject.TryGetNonEmptyStringProperty("virtualMachineName")
               let virtualMachineName = new VirtualMachineName(virtualMachineNameString)
               from virtualMachineSkuString in jsonObject.TryGetNonEmptyStringProperty("virtualMachineSku")
               let virtualMachineSku = new VirtualMachineSku(virtualMachineSkuString)
               select new VirtualMachine(virtualMachineName, virtualMachineSku);
    }
}
