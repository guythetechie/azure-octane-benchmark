using common;

namespace benchmark;

public record DiagnosticId : NonEmptyString
{
    public DiagnosticId(string value) : base(value) { }
}