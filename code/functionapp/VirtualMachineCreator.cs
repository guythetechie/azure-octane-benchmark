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

public class VirtualMachineCreator
{
    private readonly CreateVirtualMachine createVirtualMachine;
    private readonly QueueOctaneBenchmark queueOctaneBenchmark;

    public VirtualMachineCreator(CreateVirtualMachine createVirtualMachine, QueueOctaneBenchmark queueOctaneBenchmark)
    {
        this.createVirtualMachine = createVirtualMachine;
        this.queueOctaneBenchmark = queueOctaneBenchmark;
    }

    [FunctionName("create-virtual-machine")]
    public async Task Run([ServiceBusTrigger("%SERVICE_BUS_CREATE_VM_QUEUE_NAME%", Connection = "ServiceBusConnection", AutoCompleteMessages = false)] ServiceBusReceivedMessage message, ServiceBusMessageActions messageActions, ILogger logger, CancellationToken cancellationToken)
    {
        await JsonObjectModule.FromBinaryData(message.Body)
                              .Do(requestJson => logger.LogInformation("Request payload: {CreateVirtualMachineRequestJson}", requestJson.SerializeToString()))
                              .Bind(DeserializeToVirtualMachine)
                              .Do(_ => logger.LogInformation("Creating virtual machine..."))
                              .Do(virtualMachine => createVirtualMachine(virtualMachine, cancellationToken))
                              .Do(_ => logger.LogInformation("Queuing virtual machine for Octane benchmark..."))
                              .Do(virtualMachine => queueOctaneBenchmark(virtualMachine.Name, cancellationToken))
                              .Do(_ => logger.LogInformation("Completing service bus message..."))
                              .MapAsync(async _ =>
                              {
                                  await messageActions.CompleteMessageAsync(message, cancellationToken);
                                  return Unit.Default;
                              })
                              .Timeout(TimeSpan.FromMinutes(4.5))
                              .Catch(Errors.TimedOut, Aff(async () =>
                              {
                                  logger.LogWarning("Function app timed out, abandoning message...");
                                  await messageActions.AbandonMessageAsync(message, cancellationToken: cancellationToken);
                                  return Unit.Default;
                              }))
                              .Catch(CommonErrorCode.InvalidJson, error => Aff(async () =>
                              {
                                  logger.LogCritical($"JSON is invalid. Error is '{error.Message}'. Sending message to dead-letter queue...");
                                  await messageActions.DeadLetterMessageAsync(message, deadLetterReason: nameof(CommonErrorCode.InvalidJson), deadLetterErrorDescription: error.Message, cancellationToken);
                                  return Unit.Default;
                              }))
                              .Catch(exception => exception is Azure.RequestFailedException requestFailedException && (requestFailedException.Status == 400), exception => Aff(async () =>
                              {
                                  var requestFailedException = (Azure.RequestFailedException)exception;
                                  logger.LogCritical($"Azure request failed. Error is '{requestFailedException.Message}'. Sending message to dead-letter queue...");
                                  await messageActions.DeadLetterMessageAsync(message, deadLetterReason: requestFailedException.ErrorCode, deadLetterErrorDescription: requestFailedException.Message, cancellationToken);
                                  return Unit.Default;
                              }))
                              .RunAndThrowIfFail();
    }

    private static Eff<VirtualMachine> DeserializeToVirtualMachine(JsonObject jsonObject)
    {
        return from virtualMachineNameString in jsonObject.TryGetNonEmptyStringProperty("virtualMachineName")
               let virtualMachineName = new VirtualMachineName(virtualMachineNameString)
               from virtualMachineSkuString in jsonObject.TryGetNonEmptyStringProperty("virtualMachineSku")
               let virtualMachineSku = new VirtualMachineSku(virtualMachineSkuString)
               select new VirtualMachine(virtualMachineName, virtualMachineSku);
    }
}
