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