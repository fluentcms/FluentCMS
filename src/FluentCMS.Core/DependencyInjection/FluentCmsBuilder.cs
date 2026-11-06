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
using FluentCMS.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

public sealed class FluentCmsBuilder
{
    private readonly List<IPlugin> _plugins = [];

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

    public SsoOptions Sso { get; } = new();

    public ReverseProxyOptions ReverseProxy { get; } = new();

    // The address users reach the instance at, such as https://cms.example.com, which links in emails and the issuer are
    // built from. FluentCms:PublicOrigin wins over it.
    public string? PublicOrigin { get; set; }

    public DeliveryCacheOptions DeliveryCache { get; } = new();

    public OpenApiDocumentOptions OpenApi { get; } = new();

    internal IDataProvider? DataProvider { get; private set; }

    internal Assembly? ContentMigrationsAssembly { get; private set; }

    internal bool SchemaSyncOutsideDevelopment { get; private set; }

    internal IReadOnlyList<IPlugin> Plugins => _plugins;

    // Creates the plugin now and lets configure set it up. A plugin that a referenced assembly also names is added once, with
    // this configuration and at this place in the order.
    public FluentCmsBuilder AddPlugin<TPlugin>(Action<TPlugin>? configure = null)
        where TPlugin : class, IPlugin, new()
    {
        var plugin = new TPlugin();
        configure?.Invoke(plugin);
        _plugins.Add(plugin);
        return this;
    }

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
