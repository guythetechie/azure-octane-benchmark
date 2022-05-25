using LanguageExt;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;

public record NonEmptyString
{
    private readonly string value;

    public NonEmptyString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{nameof(value)}' cannot be null or whitespace.", nameof(value));
        }

        this.value = value;
    }

    public sealed override string ToString()
    {
        return value;
    }

    public static implicit operator string(NonEmptyString nonEmptyString)
    {
        return nonEmptyString.value;
    }
}

/// <summary>
/// Wrapper for strings that represent an absolute URL.
/// </summary>
public abstract record UriRecord : NonEmptyString
{
    protected UriRecord(string value) : base(value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var _) is false)
        {
            throw new ArgumentException($"{value} is not a valid absolute URI.", nameof(value));
        }
    }

    public Uri ToUri() => new(this.ToString(), UriKind.Absolute);
}

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