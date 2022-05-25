using LanguageExt;
using Microsoft.Extensions.Logging;

namespace functionapp;

public static class ILoggerModule
{
    public static Unit LogInformationUnit(this ILogger logger, string message, params object[] args)
    {
        logger.LogInformation(message, args);
        return Unit.Default;
    }
}