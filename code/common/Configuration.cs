using LanguageExt;
using LanguageExt.Common;
using Microsoft.Extensions.Configuration;

namespace common;

public static class ConfigurationModule
{
    public static NonEmptyString GetNonEmptyValue(this IConfiguration configuration, string key)
    {
        return configuration.TryGetNonEmptyValue(key)
                            .Run()
                            .ThrowIfFail();
    }

    public static Eff<NonEmptyString> TryGetNonEmptyValue(this IConfiguration configuration, string key)
    {
        return configuration.TryGetValue(key)
                            .Bind(value => string.IsNullOrWhiteSpace(value)
                                            ? Prelude.FailEff<NonEmptyString>(Error.New($"Configuration key '{key}' has a null, empty or whitespace value."))
                                            : Prelude.SuccessEff(new NonEmptyString(value)));
    }

    public static Eff<string> TryGetValue(this IConfiguration configuration, string key)
    {
        return configuration.GetOptionalValue(key)
                            .ToEff(Error.New($"Could not find '{key}' in configuration."));
    }

    public static Option<string> GetOptionalValue(this IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);

        return section.Exists() ? section.Value : Prelude.None;
    }
}
