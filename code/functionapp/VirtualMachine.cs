using LanguageExt;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;

public record VirtualMachineSku : NonEmptyString
{
    public VirtualMachineSku(string value) : base(value) { }
}

public record VirtualMachineName : NonEmptyString
{
    public VirtualMachineName(string value) : base(value) { }

}

public record VirtualMachine(VirtualMachineName Name, VirtualMachineSku Sku);

public delegate ValueTask<Unit> QueueVirtualMachineCreation(Seq<(VirtualMachine VirtualMachine, DateTimeOffset EnqueueAt)> virtualMachines, CancellationToken cancellationToken);

public delegate ValueTask<Unit> CreateVirtualMachine(VirtualMachine virtualMachine, CancellationToken cancellationToken);