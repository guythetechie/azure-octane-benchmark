using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace common;

public static class ServiceProviderModule
{
    public static Option<T> GetOptionalService<T>(this IServiceProvider provider)
    {
        var service = provider.GetService<T>();

        return service is null ? Prelude.None : service;
    }
}
