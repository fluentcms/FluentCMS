using System.Reflection;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Api.OpenApi;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Seeding;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

public sealed class FluentCmsBuilder
{
    internal FluentCmsBuilder(IServiceCollection services)
    {
        Services = services;
        Jobs = new JobsBuilder(services);
        Events = new EventsBuilder(services, Activity);
        Seeding = new SeedingBuilder(services);
    }

    public IServiceCollection Services { get; }

    public AuthorizationBuilder Authorization { get; } = new();

    public JobsBuilder Jobs { get; }

    public EventsBuilder Events { get; }

    public ActivityOptions Activity { get; } = new();

    public SettingsBuilder Settings { get; } = new();

    public SeedingBuilder Seeding { get; }

    public AdminUserOptions Users { get; } = new();

    public ClientOptions Clients { get; } = new();

    public ReverseProxyOptions ReverseProxy { get; } = new();

    public DeliveryCacheOptions DeliveryCache { get; } = new();

    public OpenApiDocumentOptions OpenApi { get; } = new();

    internal IDataProvider? DataProvider { get; private set; }

    internal Assembly? ContentMigrationsAssembly { get; private set; }

    internal bool SchemaSyncOutsideDevelopment { get; private set; }

    // For a host whose types are data, such as a schema file, and so have no compiled migrations: SchemaSync defaults to
    // Auto in every environment, with the rules of Development. SQLite only; configuration cannot turn this on.
    public FluentCmsBuilder AllowSchemaSyncOutsideDevelopment()
    {
        SchemaSyncOutsideDevelopment = true;
        return this;
    }

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
