using common;
using LanguageExt;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;

public record VirtualMachineCreationQueueName
{
    public VirtualMachineCreationQueueName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Virtual machine creation queue name cannot be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public record OctaneBenchmarkQueueName
{
    public OctaneBenchmarkQueueName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Octane benchmark queue name cannot be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public record VirtualMachineDeletionQueueName
{
    public VirtualMachineDeletionQueueName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Virtual machine deletion queue name cannot be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public delegate ValueTask QueueVirtualMachineCreation(Seq<(VirtualMachine VirtualMachine, DateTimeOffset EnqueueAt)> virtualMachines, CancellationToken cancellationToken);

public delegate ValueTask CreateVirtualMachine(VirtualMachine virtualMachine, CancellationToken cancellationToken);

public delegate ValueTask QueueOctaneBenchmark(VirtualMachine virtualMachine, CancellationToken cancellationToken);

public delegate ValueTask RunOctaneBenchmark(Microsoft.Extensions.Logging.ILogger logger, VirtualMachine virtualMachine, DiagnosticId diagnosticId, CancellationToken cancellationToken);

public delegate ValueTask QueueVirtualMachineDeletion(VirtualMachineName virtualMachineName, CancellationToken cancellationToken);

public delegate ValueTask DeleteVirtualMachine(VirtualMachineName virtualMachineName, CancellationToken cancellationToken);