using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

[assembly: CLSCompliant(false)]
[assembly: SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "We use private nested types to simulate discriminated unions.")]
[assembly: SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "We're using nullable reference types.")]
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "<Pending>")]
[assembly: SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "<Pending>")]
[assembly: SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "<Pending>")]
[assembly: SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "<Pending>")]
namespace benchmark;

public static class Program
{
    public static async Task Main(string[] arguments)
    {
        await Host.CreateDefaultBuilder(arguments)
                  .ConfigureServices(ConfigureServices)
                  .Build()
                  .RunAsync();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.AddHttpClient();
        services.AddHostedService<Worker>();
        services.AddSingleton(ServiceProviderModule.GetDiagnosticId)
                .AddSingleton(ServiceProviderModule.GetVirtualMachineSku)
                .AddSingleton(ServiceProviderModule.GetEdgeDriverFactory);
    }
}