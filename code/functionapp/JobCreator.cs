using LanguageExt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;

public class JobCreator
{
    private readonly QueueVirtualMachineCreation queueVirtualMachineCreation;

    public JobCreator(QueueVirtualMachineCreation queueVirtualMachineCreation)
    {
        this.queueVirtualMachineCreation = queueVirtualMachineCreation;
    }

    [FunctionName("create-job")]
    public Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest request, ILogger logger, CancellationToken cancellationToken)
    {
        return JsonObjectModule.FromStream(request.Body, cancellationToken)
                               .Do(requestJson => logger.LogInformation("Request payload: {CreateJobRequestJson}", requestJson.SerializeToString()))
                               .Bind(DeserializeRequestJson)
                               .Map(GenerateVirtualMachines)
                               .Map(GenerateVirtualMachineQueuePayload)
                               .Do(virtualMachines => logger.LogInformation("Total virtual machines requested: {VirtualMachineCount}", virtualMachines.Length))
                               .Do(_ => logger.LogInformation("Queuing virtual machine creation..."))
                               .Do(virtualMachines => queueVirtualMachineCreation(virtualMachines, cancellationToken))
                               .Map(_ => new NoContentResult() as IActionResult)
                               .IfFail(ErrorModule.ToActionResult)
                               .RunAndThrowIfFail()
                               .AsTask();
    }

    private static Eff<CreateJobRequest> DeserializeRequestJson(JsonObject jsonObject)
    {
        return from virtualMachineSequence in jsonObject.TryGetJsonObjectArray("virtualMachines")
               from virtualMachines in virtualMachineSequence.Map(jsonObject => from skuString in jsonObject.TryGetNonEmptyStringProperty("sku")
                                                                                let sku = new VirtualMachineSku(skuString)
                                                                                from count in jsonObject.TryGetUIntProperty("count")
                                                                                select (Sku: sku, Count: count))
                                                             .Sequence()
               select new CreateJobRequest(virtualMachines);
    }

    private static Seq<VirtualMachine> GenerateVirtualMachines(CreateJobRequest request)
    {
        return request.VirtualMachines.Bind(virtualMachine => GenerateVirtualMachines(virtualMachine.Sku, virtualMachine.Count));
    }

    private static Seq<VirtualMachine> GenerateVirtualMachines(VirtualMachineSku sku, uint count)
    {
        return Seq.repeat(sku, (int)count)
                  .Map(sku => new VirtualMachine(Name: GenerateVirtualMachineName(), Sku: sku));
    }

    private static VirtualMachineName GenerateVirtualMachineName()
    {
        var suffix = Path.GetRandomFileName().Replace(".", "", StringComparison.OrdinalIgnoreCase)[..5];
        return new VirtualMachineName($"octane-{suffix}");
    }

    private static Seq<(VirtualMachine VirtualMachine, DateTimeOffset EnqueueAt)> GenerateVirtualMachineQueuePayload(Seq<VirtualMachine> virtualMachines)
    {
#pragma warning disable CA5394 // Do not use insecure randomness
        return virtualMachines.Map(virtualMachine => (virtualMachine, DateTimeOffset.UtcNow.AddSeconds(Random.Shared.NextDouble() * 60)));
#pragma warning restore CA5394 // Do not use insecure randomness
    }

    private record CreateJobRequest(Seq<(VirtualMachineSku Sku, uint Count)> VirtualMachines);
}