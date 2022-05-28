using common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace benchmark;

internal static class ServiceProviderModule
{
    public static EdgeDriverFactory GetEdgeDriverFactory(IServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        return new EdgeDriverFactory(factory);
    }

    public static DiagnosticId GetDiagnosticId(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var diagnosticId = configuration.GetNonEmptyValue("DIAGNOSTIC_ID");

        return new DiagnosticId(diagnosticId);
    }

    public static VirtualMachineSku GetVirtualMachineSku(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var sku = configuration.GetNonEmptyValue("VIRTUAL_MACHINE_SKU");

        return new VirtualMachineSku(sku);
    }
}