using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;

[assembly: FunctionsStartup(typeof(functionapp.Startup))]
[assembly: CLSCompliant(false)]
[assembly: SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "We use private nested types to simulate discriminated unions.")]
[assembly: SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "We're using nullable reference types.")]
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "<Pending>")]
[assembly: SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "<Pending>")]
[assembly: SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "<Pending>")]
namespace functionapp;

public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        ConfigureServices(builder.Services);
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(ServiceProviderModule.GetAzureAuthorityUri)
                .AddSingleton(ServiceProviderModule.GetTokenCredential)
                .AddSingleton(ServiceProviderModule.GetServiceBusClient)
                .AddSingleton(ServiceProviderModule.GetVirtualMachineCreationQueueName)
                .AddTransient(ServiceProviderModule.GetQueueVirtualMachineCreation)
                .AddSingleton(ServiceProviderModule.GetArmClient)
                .AddTransient(ServiceProviderModule.GetCreateVirtualMachine);
    }
}