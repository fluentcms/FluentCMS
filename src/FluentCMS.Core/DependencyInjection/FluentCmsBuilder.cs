using System.Reflection;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

public sealed class FluentCmsBuilder
{
    internal FluentCmsBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    public AuthorizationBuilder Authorization { get; } = new();

    internal IDataProvider? DataProvider { get; private set; }

    internal Assembly? ContentMigrationsAssembly { get; private set; }

    // The assembly that holds the host's content migrations, if it is not the entry assembly.
    public FluentCmsBuilder UseContentMigrations(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        ContentMigrationsAssembly = assembly;
        return this;
    }

    // The seam for a data provider package, whose own UseXxx method calls this.
    public FluentCmsBuilder UseDataProvider(IDataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (DataProvider is not null)
        {
            throw new FluentCmsConfigurationException(
                StorageErrorCodes.SeveralDataProviders,
                $"AddFluentCms got a second data provider, {provider.Name}, after {DataProvider.Name}; a host uses exactly one.");
        }

        DataProvider = provider;
        return this;
    }
}
