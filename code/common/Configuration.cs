using LanguageExt;
using Microsoft.Extensions.Configuration;

namespace common;

public static class ConfigurationModule
{
    public static string GetNonEmptyValue(this IConfiguration configuration, string key)
    {
        return configuration.GetOptionalNonEmptyValue(key)
                            .IfNoneThrow($"Configuration key '{key}' is missing or has a null, empty or whitespace value.");
    }

    public static Option<string> GetOptionalNonEmptyValue(this IConfiguration configuration, string key)
    {
        return configuration.GetOptionalSection(key)
                            .Map(section => section.Value)
                            .Where(value => string.IsNullOrWhiteSpace(value) is false);
    }

    public static string GetValue(this IConfiguration configuration, string key)
    {
        return configuration.GetOptionalValue(key)
                            .IfNoneThrow($"Could not find key '{key}' in configuration.");
    }

    public static Option<string> GetOptionalValue(this IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);

        return section.Exists() ? section.Value : Prelude.None;
    }

    public static IConfigurationSection GetSection(IConfiguration configuration, string key)
    {
        return configuration.GetOptionalSection(key)
                            .IfNoneThrow($"Could not find section with key '{key}' in configuration.");
    }

    public static Option<IConfigurationSection> GetOptionalSection(this IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);

        return section.Exists() ? Prelude.Some(section) : Prelude.None;
    }
}
