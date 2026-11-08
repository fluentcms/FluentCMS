using System.Reflection;
using FluentCMS.Activity;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Api.OpenApi;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Caching;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Events;
using FluentCMS.Core.Health;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Seeding;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Localization;
using FluentCMS.Plugins;
using FluentCMS.Providers;
using FluentCMS.Schema;
using FluentCMS.Seeding;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.DependencyInjection;

public static class FluentCmsServiceCollectionExtensions
{
    private const string JobsSection = "Jobs";
    private const string OutboxSection = "Events:Outbox";
    private const string ActivitySection = "Activity";

    public static IServiceCollection AddFluentCms(this IServiceCollection services, Action<FluentCmsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new FluentCmsBuilder(services);
        configure(builder);

        var provider = builder.DataProvider ?? throw new FluentCmsConfigurationException(
            StorageErrorCodes.NoDataProvider,
            "AddFluentCms has no data provider. Add the package for your database and call cms.UseSqlite(...), cms.UsePostgreSql(...) or cms.UseSqlServer(...) inside AddFluentCms.");

        if (builder.SchemaSyncOutsideDevelopment && !provider.SupportsSchemaSync)
        {
            throw new FluentCmsConfigurationException(
                StorageErrorCodes.SchemaSyncNotSupported,
                $"AllowSchemaSyncOutsideDevelopment needs a data provider with schema sync, and {provider.Name} has none; its content tables come from migrations.");
        }

        // Before the services of core, so that a plugin's replaces one that core adds only when none is there.
        var plugins = PluginHost.ConfigureServices(builder, PluginHost.Compose(builder.Plugins, PluginDiscovery.Types, PluginHost.RunningSdk));
        services.AddSingleton(plugins);
        services.TryAddSingleton<IHookTiebreaker>(plugins);
        builder.Providers.BeforeCore(services);
        services.AddSingleton<PluginState>();
        services.AddSingleton<IPluginState>(serviceProvider => serviceProvider.GetRequiredService<PluginState>());
        services.AddScoped<PluginAdministration>();
        foreach (var storage in plugins.Contributions.Storage)
        {
            storage.Register(services, provider);
        }

        services.AddSingleton(new ErrorCodeCatalogue(plugins.Contributions.ErrorCodes));

        services.AddProductionStartupChecks();
        services.AddFluentCmsOptions(DatabaseOptions.Section, StorageErrorCodes.InvalidDatabaseSetting, new DatabaseOptions { SchemaSyncOutsideDevelopment = builder.SchemaSyncOutsideDevelopment });
        services.AddFluentCmsOptions(SecurityOptions.Section, HostErrorCodes.InvalidOption, new SecurityOptions(), security => security.Validate());
        services.AddFluentCmsOptions(ReverseProxyOptions.Section, HostErrorCodes.InvalidOption, builder.ReverseProxy, proxy => proxy.Validate());
        services.AddReverseProxyHttpsPolicy();
        services.AddOptions<PublicOrigin>()
            .Configure<IServiceProvider>((origin, container) => origin.Resolve(
                container.GetService<IConfiguration>()?[$"{FluentCmsOptions.Section}:{PublicOrigin.Key}"] is { Length: > 0 } configured ? configured : builder.PublicOrigin,
                container.GetRequiredService<ReverseProxyOptions>(),
                container.GetService<IHostEnvironment>()))
            .ValidateOnStart();
        services.TryAddSingleton(container => container.GetRequiredService<IOptions<PublicOrigin>>().Value);
        services.AddSecurityDefaults();
        services.AddFluentCmsApiProtection();

        services.AddSingleton(provider);
        services.AddSingleton(provider.Conventions);
        services.AddSingleton(provider.JsonDialect);
        services.AddSingleton(provider.JobClaimDialect);

        // Until content types can be added to the builder, a schema comes from the container, and none means system tables only.
        // A host that builds its own with the registry of the container gets the plugins' field types.
        services.TryAddSingleton<IFieldTypeRegistry>(serviceProvider => new FieldTypeRegistry(serviceProvider.GetServices<IFieldType>()));
        services.TryAddSingleton(serviceProvider => new SchemaBuilder(serviceProvider.GetRequiredService<IFieldTypeRegistry>()).Build());

        builder.Seeding.Validate();
        if (builder.Seeding.Types.Count > 0)
        {
            builder.Settings.Add<SeedingState>();
        }

        builder.Settings.Add<BrandingSettings>();
        var settings = builder.Settings.Build();
        services.AddSingleton(settings);

        services.TryAddSingleton(serviceProvider => new PermissionCatalogue(
            serviceProvider.GetRequiredService<SchemaModel>(),
            serviceProvider.GetServices<IPermissionProvider>(),
            settings.Descriptors.Select(descriptor => descriptor.Area)));

        services.AddSingleton<RoleVersionInterceptor>();

        var contentMigrations = builder.ContentMigrationsAssembly ?? Assembly.GetEntryAssembly() ?? typeof(CmsDbContext).Assembly;
        var pluginModels = plugins.Contributions.Models;
        services.AddDbContext<CmsDbContext>((serviceProvider, options) =>
        {
            ContentMigrations.Configure(options, provider, serviceProvider.GetRequiredService<SchemaModel>(), contentMigrations, pluginModels);
            options.AddInterceptors(serviceProvider.GetRequiredService<RoleVersionInterceptor>());
        });

        services.AddDbContext<SystemDbContext>(options =>
        {
            provider.ConfigureSystemMigrations(options);
            options.UseSystemModel(provider.Conventions);
        });

        services.AddLogging();
        services.AddFluentCmsProblemDetails();
        services.AddSingleton<MigrationRunner>();
        services.AddHostedService<MigrationStartupService>();
        services.AddStartupReport();

        services.AddFluentCmsOptions(ContentQueryOptions.Section, HostErrorCodes.InvalidOption, new ContentQueryOptions(), query => query.Validate());
        services.TryAddSingleton<IOperationContextAccessor, OperationContextAccessor>();
        services.TryAddSingleton<EffectiveGrantCache>();
        services.AddSingleton(serviceProvider => new RowRuleRegistry(
            serviceProvider.GetRequiredService<SchemaModel>(),
            serviceProvider.GetRequiredService<ContentQueryOptions>(),
            builder.Authorization.Rules));
        services.AddHostedService<RowRuleStartupCheck>();
        // Before the identity registration, which takes out the Data Protection hosted service that the pages' antiforgery adds.
        HostedPages.Add(services);
        IdentityRegistration.Add(services, builder.Users, builder.Clients, builder.Sso, builder.Providers);
        services.TryAddScoped<IAuthorizationEvaluator, AuthorizationEvaluator>();
        services.AddScoped<ManagementAuthority>();
        services.TryAddScoped<IPermissionChecker, PermissionChecker>();
        services.AddScoped<RoleService>();
        services.AddScoped<SuperAdminGuard>();
        services.TryAddScoped<IContentQueryAccess, ContentQueryAccess>();
        services.AddScoped<ReferenceResolvers>();
        services.AddHostedService<ReferenceResolversStartupCheck>();
        services.AddScoped<ContentQueryEngine>();

        services.TryAddSingleton(TimeProvider.System);
        builder.Providers.Default<IDistributedLock, DatabaseDistributedLock>();
        builder.Providers.Default<IDistributedCache, MemoryDistributedCache>();
        services.TryAddSingleton<SiteCache>();
        services.TryAddScoped<ISiteResolver, SiteResolver>();
        services.TryAddScoped<ILocaleResolver, LocaleResolver>();
        services.AddScoped<LocaleRegistry>();
        services.AddScoped<LocaleFallbackReader>();
        services.AddScoped<SiteManager>();
        services.AddScoped<RequestBridge>();
        services.AddDeliveryApi(builder.DeliveryCache);
        services.AddManagementContentApi();
        services.AddManagementIdentityApi();
        services.AddManagementSiteApi();
        services.AddManagementOperationsApi();
        services.AddManagementPluginApi();
        services.AddScoped<SchemaIntrospection>();
        services.TryAddScoped<ISchemaIntrospection>(serviceProvider => serviceProvider.GetRequiredService<SchemaIntrospection>());
        services.AddManagementSchemaApi();
        services.AddFluentCmsHealth();
        services.AddSystemApi();
        services.AddFluentCmsOpenApi(builder.OpenApi);

        services.AddSingleton<SettingsCache>();
        services.AddScoped<SettingsService>();
        services.TryAddScoped(typeof(ISettings<>), typeof(Settings<>));

        services.TryAddSingleton<IRichTextRenderer, RichTextRenderer>();
        services.TryAddSingleton<ContentEntryMapper>();
        services.TryAddSingleton<IHookEntryMapper>(serviceProvider => serviceProvider.GetRequiredService<ContentEntryMapper>());
        services.TryAddSingleton<EntryCanonicalizer>();
        services.TryAddSingleton<EntryComposer>();
        services.TryAddSingleton<ResponseRichText>();
        services.AddScoped<HookPipeline>();
        services.AddScoped<ValidationPipeline>();
        services.AddScoped<EntryStore>();
        services.AddScoped<ScheduleStore>();
        services.AddScoped<ContentService>();
        services.TryAddScoped<IContentService>(serviceProvider => serviceProvider.GetRequiredService<ContentService>());
        services.TryAddScoped(typeof(IContentService<>), typeof(TypedContentService<>));
        services.TryAddScoped<IContentReferences, ContentReferences>();

        services.AddFluentCmsOptions(JobsSection, JobErrorCodes.InvalidRunnerOptions, builder.Jobs.Options, jobs => jobs.Validate());
        services.AddSingleton<JobRegistry>();
        services.AddSingleton<JobStore>();
        services.AddSingleton<JobExecutor>();
        services.AddSingleton<JobWake>();
        services.AddSingleton<RecurringJobs>();
        services.AddSingleton<JobFactory>();
        services.TryAddSingleton<IJobEventSink, BusJobEventSink>();
        services.TryAddSingleton<IJobScheduler, JobScheduler>();
        services.TryAddSingleton<IJobRunner, JobRunner>();
        services.TryAddSingleton<IJobDeadLetters, JobDeadLetters>();
        services.AddScoped<JobAdministration>();
        services.AddHostedService<JobRunnerHostedService>();

        builder.Jobs.AddJob<TrashPurgeJob, TrashPurgePayload>(TrashPurgeJob.Type);
        builder.Jobs.AddJob<ScheduledPublishJob, ScheduledPayload>(ScheduledPublishJob.Type);
        builder.Jobs.AddJob<ScheduledUnpublishJob, ScheduledPayload>(ScheduledUnpublishJob.Type);
        builder.Events.AddHandler<JobDeadLetteredEvent, ScheduleDeadLetterHandler>();
        builder.Events.AddHandler<SiteUpdatedEvent, SiteCacheInvalidation>();
        services.AddHostedService<TrashPurgeScheduleService>();

        builder.Jobs.AddJob<JobRetentionJob, JobRetentionPayload>(JobRetentionJob.Type, JobRetentionJob.AttemptTimeout);
        services.AddHostedService<JobRetentionScheduleService>();

        builder.Jobs.AddJob<OidcPruneJob, OidcPrunePayload>(OidcPruneJob.Type);
        services.AddHostedService<OidcPruneScheduleService>();

        builder.Events.Outbox.Validate();
        builder.Jobs.Options.Queues.TryAdd(OutboxDrainJob.Queue, 1);
        builder.Jobs.AddJob<OutboxDrainJob, OutboxDrainPayload>(OutboxDrainJob.Type);
        builder.Jobs.AddJob<OutboxPruneJob, OutboxPrunePayload>(OutboxPruneJob.Type, OutboxPruneJob.AttemptTimeout);
        services.AddFluentCmsOptions(OutboxSection, EventErrorCodes.InvalidOutboxOptions, builder.Events.Outbox, outbox => outbox.Validate());
        builder.Events.Handlers.Validate();
        services.AddFluentCmsOptions(EventHandlerOptions.Section, HostErrorCodes.InvalidOption, builder.Events.Handlers, handlers => handlers.Validate());
        services.AddSingleton<OutboxRegistry>();
        services.AddSingleton<OutboxKick>();
        services.AddSingleton<OutboxRows>();
        services.TryAddSingleton<IEventDeadLetters, OutboxDeadLetters>();
        services.AddScoped<EventAdministration>();
        services.AddSingleton<OutboxDrainer>();
        services.AddHostedService<OutboxScheduleService>();
        services.AddScoped<EventDispatcher>();
        services.AddScoped<OutboxWriter>();
        services.AddScoped<EventBuffer>();
        services.AddScoped<PluginTransactions>();
        services.AddScoped<ITransactionProbe, ContentTransactionProbe>();
        services.TryAddScoped<IEventBus, EventBus>();
        services.AddScoped<EntryChangeSink>();
        services.AddScoped<IEntryChangeSink>(serviceProvider => serviceProvider.GetRequiredService<EntryChangeSink>());
        services.AddScoped<IEntryChangeStager>(serviceProvider => serviceProvider.GetRequiredService<EntryChangeSink>());

        builder.Activity.Validate();
        builder.Jobs.AddJob<ActivityRetentionJob, ActivityRetentionPayload>(ActivityRetentionJob.Type);
        services.AddFluentCmsOptions(ActivitySection, ActivityErrorCodes.InvalidOptions, builder.Activity, activity => activity.Validate());
        services.AddScoped<ActivityLogWriter>();
        services.TryAddScoped<IActivityLog>(serviceProvider => serviceProvider.GetRequiredService<ActivityLogWriter>());
        services.TryAddScoped<IActivityQuery, ActivityQuery>();
        services.AddScoped<ActivityReader>();
        ActivityEvents.Register(builder.Events);
        services.AddHostedService<ActivityScheduleService>();

        services.AddOutputCache();
        builder.Providers.Default(MemoryOutputCacheStore());
        builder.Providers.Default<ISearchIndex, NoSearchIndex>();
        OutputCacheEviction.Register(builder.Events, () => builder.DeliveryCache.OutputCache);

        services.AddSingleton(builder.Seeding);
        services.AddScoped<SeedStateStore>();
        services.AddScoped<ISeedRunner, SeedRunner>();
        if (builder.Seeding.Types.Count > 0 && builder.Seeding.RunOnStart)
        {
            services.AddHostedService<SeedStartupService>();
        }

        // Last, so that a replacement wins over every registration of its slot that came before.
        services.AddSingleton(builder.Providers.Apply(services));

        return services;
    }

    // The framework's in-memory store is internal, so the slot's default is the registration AddOutputCache makes for it.
    private static ServiceDescriptor MemoryOutputCacheStore() =>
        new ServiceCollection().AddOutputCache().Single(descriptor => ProvidersBuilder.Fills(descriptor, typeof(IOutputCacheStore)));
}
