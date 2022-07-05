using System;
using System.Diagnostics.CodeAnalysis;

[assembly: CLSCompliant(false)]
[assembly: SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "We use private nested types to simulate discriminated unions.")]
[assembly: SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "We're using nullable reference types.")]
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "<Pending>")]
[assembly: SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "<Pending>")]
[assembly: SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "<Pending>")]
[assembly: SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "<Pending>")]
namespace common;

/// <summary>
/// Wrapper for strings that represent an absolute URL.
/// </summary>
public abstract record UriRecord
{
    protected UriRecord(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var _) is false)
        {
            throw new ArgumentException($"{value} is not a valid absolute URI.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public Uri Uri => new(Value, UriKind.Absolute);

    public override string ToString() => Value;
}

public record VirtualMachineSku
{
    public VirtualMachineSku(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Virtual machine SKU cannot be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public record VirtualMachineName
{
    public VirtualMachineName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Virtual machine name cannot be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public record DiagnosticId
{
    public DiagnosticId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Diagnostic ID cannot be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public record VirtualMachine(VirtualMachineName Name, VirtualMachineSku Sku);