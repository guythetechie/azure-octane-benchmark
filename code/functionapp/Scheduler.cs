using common;
using LanguageExt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using static LanguageExt.Prelude;

namespace functionapp;

public class Scheduler
{
    private readonly QueueVirtualMachineCreation queueVirtualMachineCreation;

    public Scheduler(QueueVirtualMachineCreation queueVirtualMachineCreation)
    {
        this.queueVirtualMachineCreation = queueVirtualMachineCreation;
    }

    [FunctionName("scheduler")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest request, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Getting JSON from request...");
            var requestJson = await JsonObjectModule.FromStream(request.Body, cancellationToken);
            logger.LogInformation("Received payload {CreateJobRequestJson}", requestJson.SerializeToString());

            logger.LogInformation("Deserializing request JSON...");
            var skuCounts = DeserializeJson(requestJson);

            logger.LogInformation("Generating queue payload...");
            var virtualMachines = GenerateVirtualMachines(skuCounts);
            var queuePayload = GenerateVirtualMachineQueuePayload(virtualMachines);
            logger.LogInformation("Total virtual machines requested: {VirtualMachineCount}", virtualMachines.Length);

            logger.LogInformation("Queuing virtual machine creation...");
            await queueVirtualMachineCreation(queuePayload, cancellationToken);

            return new NoContentResult();
        }
        catch (JsonException jsonException)
        {
            return new BadRequestObjectResult(jsonException.Message);
        }
    }

    private static ImmutableDictionary<VirtualMachineSku, long> DeserializeJson(JsonObject jsonObject)
    {
        var skuCounts =
            from node in jsonObject.GetJsonArrayProperty("virtualMachines")
            let nodeJsonObject = node is JsonObject jsonObject
                                    ? jsonObject
                                    : throw new JsonException($"Property 'virtualMachines' has an element that is null or not a JSON object.")
            let skuText = nodeJsonObject.GetNonEmptyStringProperty("sku")
            let sku = new VirtualMachineSku(skuText)
            let count = nodeJsonObject.GetUIntProperty("count")
            select (sku, count);

        return skuCounts.GroupBy(pair => pair.sku)
                        .ToImmutableDictionary(grouping => grouping.Key,
                                               grouping => grouping.Sum(pair => pair.count));
    }

    private static Seq<VirtualMachine> GenerateVirtualMachines(IDictionary<VirtualMachineSku, long> skuCounts)
    {
        var virtualMachines =
            from keyValuePair in skuCounts
            from sku in Enumerable.Repeat(keyValuePair.Key, (int) keyValuePair.Value)
            let name = GenerateVirtualMachineName()
            select new VirtualMachine(name, sku);

        return virtualMachines.ToSeq();
    }

    private static VirtualMachineName GenerateVirtualMachineName()
    {
        var suffix = Path.GetRandomFileName().Replace(".", "", StringComparison.OrdinalIgnoreCase)[..5];
        return new VirtualMachineName($"octane-{suffix}");
    }

    private static Seq<(VirtualMachine VirtualMachine, DateTimeOffset EnqueueAt)> GenerateVirtualMachineQueuePayload(Seq<VirtualMachine> virtualMachines)
    {
        return from virtualMachine in virtualMachines
               // Goal is approximately 1 VM every 10 seconds
               let enqueueAt = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.NextDouble() * 10 * virtualMachines.Length)
               select (virtualMachine, enqueueAt);
    }
}