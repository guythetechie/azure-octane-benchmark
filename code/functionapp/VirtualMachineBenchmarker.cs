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
        Eff<JsonObject> tryGetJsonObject() => JsonObjectModule.FromBinaryData(message.Body);

        Eff<DiagnosticId> tryGetDiagnosticId() => message.ApplicationProperties.TryGetValue("Diagnostic-Id")
                                                                               .Map(value => value.ToString())
                                                                               .Filter(value => string.IsNullOrWhiteSpace(value) is false)
                                                                               .Map(value => new DiagnosticId(value!))
                                                                               .ToEff(Error.New("Diagnostic ID does not exist or is empty."));

        async ValueTask<Unit> completeMessage() => await messageActions.CompleteMessageAsync(message, cancellationToken)
                                                                       .ToUnit();

        var handleTimeout = Aff(async () =>
        {
            logger.LogWarning("Function app timed out, abandoning message...");

            return await messageActions.AbandonMessageAsync(message, cancellationToken: cancellationToken)
                                       .ToUnit();
        });

        async ValueTask<Unit> deadLetterMessage(string deadLetterReason, string deadLetterErrorDescription) =>
            await messageActions.DeadLetterMessageAsync(message, deadLetterReason, deadLetterErrorDescription, cancellationToken)
                                .ToUnit();

        Aff<Unit> handleInvalidJson(Error error) =>
            unitAff.Do(_ => logger.LogCritical($"JSON is invalid. Error is '{error.Message}'. Sending message to dead-letter queue..."))
                   .Do(_ => deadLetterMessage(deadLetterReason: nameof(CommonErrorCode.InvalidJson), deadLetterErrorDescription: error.Message));

        bool isUnretryableRequestFailedException(Exception exception) =>
            exception is RequestFailedException requestFailedException && (requestFailedException.Status == 400);

        Aff<Unit> handleRequestFailedException(Exception exception) =>
            SuccessAff(exception)
                .Map(exception => (RequestFailedException)exception)
                .Do(requestFailedException => logger.LogCritical($"Azure request failed. Error is '{requestFailedException.Message}'. Sending message to dead-letter queue..."))
                .Do(requestFailedException => deadLetterMessage(deadLetterReason: requestFailedException.ErrorCode, deadLetterErrorDescription: requestFailedException.Message))
                .ToUnit();

        await tryGetJsonObject().Do(jsonObject => logger.LogInformation("Request payload: {RunOctaneBenchmarkRequestJson}", jsonObject.SerializeToString()))
                                .Bind(GetVirtualMachine)
                                .Do(_ => logger.LogInformation("Getting diagnostic ID..."))
                                .Bind(virtualMachine => tryGetDiagnosticId().Map(diagnosticId => (virtualMachine, diagnosticId)))
                                .Do(_ => logger.LogInformation("Running Octane benchmark..."))
                                .Do(tuple => runOctaneBenchmark(tuple.virtualMachine, tuple.diagnosticId, cancellationToken))
                                .Do(_ => logger.LogInformation("Queueing virtual machine for deletion..."))
                                .Do(tuple => queueVirtualMachineDeletion(tuple.virtualMachine.Name, cancellationToken))
                                .Do(_ => logger.LogInformation("Completing service bus message.."))
                                .Do(_ => completeMessage())
                                .ToUnit()
                                .Timeout(TimeSpan.FromMinutes(4.5))
                                .Catch(Errors.TimedOut, _ => handleTimeout)
                                .Catch(CommonErrorCode.InvalidJson, handleInvalidJson)
                                .Catch(isUnretryableRequestFailedException, handleRequestFailedException)
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
