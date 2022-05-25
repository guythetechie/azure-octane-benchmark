using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace functionapp;

public record AzureAuthorityUri : UriRecord
{
    public AzureAuthorityUri(string value) : base(value) { }
}

public static class ServiceProviderModule
{
    public static AzureAuthorityUri GetAzureAuthorityUri(IServiceProvider provider)
    {
        var uriOption = from configuration in provider.GetOptionalService<IConfiguration>()
                        from environment in configuration.GetOptionalValue("AZURE_ENVIRONMENT")
                        select
                          environment switch
                          {
                              nameof(AzureAuthorityHosts.AzureChina) => AzureAuthorityHosts.AzureChina,
                              nameof(AzureAuthorityHosts.AzurePublicCloud) => AzureAuthorityHosts.AzurePublicCloud,
                              nameof(AzureAuthorityHosts.AzureGermany) => AzureAuthorityHosts.AzureGermany,
                              nameof(AzureAuthorityHosts.AzureGovernment) => AzureAuthorityHosts.AzureGovernment,
                              _ => throw new InvalidOperationException($"'{environment}' is not a valid Azure Authority host.")
                          };

        var uri = uriOption.IfNone(AzureAuthorityHosts.AzurePublicCloud);

        return new AzureAuthorityUri(uri.ToString());
    }

    public static TokenCredential GetTokenCredential(IServiceProvider provider)
    {
        var authority = provider.GetRequiredService<AzureAuthorityUri>();
        var options = new DefaultAzureCredentialOptions()
        {
            AuthorityHost = authority.ToUri()
        };

        return new DefaultAzureCredential(options);
    }

    public static ServiceBusClient GetServiceBusClient(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        return configuration.TryGetNonEmptyValue("SERVICE_BUS_CONNECTION_STRING")
                            .Map(connectionString => new ServiceBusClient(connectionString))
                            .IfFail(_ => GetServiceBusClientFromTokenCredential(provider))
                            .Run()
                            .ThrowIfFail();
    }

    public static VirtualMachineCreationQueueName GetVirtualMachineCreationQueueName(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        return new VirtualMachineCreationQueueName(configuration.GetNonEmptyValue("SERVICE_BUS_CREATE_VM_QUEUE_NAME"));
    }

    public static QueueVirtualMachineCreation GetQueueVirtualMachineCreation(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();

        var client = configuration.TryGetNonEmptyValue("SERVICE_BUS_CREATE_VM_QUEUE_CONNECTION_STRING")
                             .Map(connectionString => new ServiceBusClient(connectionString))
                             .IfFail(_ => provider.GetRequiredService<ServiceBusClient>())
                             .Run()
                             .ThrowIfFail();

        var queue = provider.GetRequiredService<VirtualMachineCreationQueueName>();

        return (virtualMachines, cancellationToken) => ServiceBusModule.QueueVirtualMachineCreation(client, queue, virtualMachines, cancellationToken);
    }

    private static ServiceBusClient GetServiceBusClientFromTokenCredential(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var serviceBusNamespace = configuration.GetNonEmptyValue("ServiceBusConnection__fullyQualifiedNamespace");
        var credential = provider.GetRequiredService<TokenCredential>();

        return new ServiceBusClient(serviceBusNamespace, credential);
    }

    private static Option<T> GetOptionalService<T>(this IServiceProvider provider)
    {
        var service = provider.GetService<T>();

        return service is null ? Prelude.None : service;
    }

    private static NonEmptyString GetNonEmptyValue(this IConfiguration configuration, string key)
    {
        return configuration.TryGetNonEmptyValue(key)
                            .Run()
                            .ThrowIfFail();
    }

    private static Eff<NonEmptyString> TryGetNonEmptyValue(this IConfiguration configuration, string key)
    {
        return configuration.TryGetValue(key)
                            .Bind(value => string.IsNullOrWhiteSpace(value)
                                            ? Prelude.FailEff<NonEmptyString>(Error.New($"Configuration key '{key}' has a null, empty or whitespace value."))
                                            : Prelude.SuccessEff(new NonEmptyString(value)));
    }

    private static Eff<string> TryGetValue(this IConfiguration configuration, string key)
    {
        return configuration.GetOptionalValue(key)
                            .ToEff(Error.New($"Could not find '{key}' in configuration."));
    }

    private static Option<string> GetOptionalValue(this IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);

        return section.Exists() ? section.Value : Prelude.None;
    }
}