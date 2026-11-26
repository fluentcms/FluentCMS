using System.Reflection;
using FluentCMS.Activity;
using FluentCMS.Agents;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Api.Members;
using FluentCMS.Core.Api.OpenApi;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Caching;
using FluentCMS.Core.Commands;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Events;
using FluentCMS.Core.Health;
using FluentCMS.Core.Idempotency;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Identity.PreviewTokens;
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
using FluentCMS.Core.Transfer;
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

    // A scheduled publish or unpublish changes one entry, so this is far past what it takes; it only frees the worker of one
    // that hangs. The runner's rules then retry it, and on its last attempt it is dead-lettered and its schedule failed.
    private static readonly TimeSpan ScheduledChangeTimeout = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddFluentCms(this IServiceCollection services, Action<FluentCmsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // First, so that a host started with a command that it does not run stops before anything else of it starts.
        services.TryAddSingleton(ProcessArguments.Current());
        services.AddHostedService<CommandEntryPointCheck>();

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
        services.AddSingleton<IMcpProviders>(plugins.Contributions);
        services.AddSingleton<ProtectedResources>();
        services.TryAddSingleton<IProblemDescriber, ProblemDescriber>();

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
        builder.Settings.Add<PreviewSettings>();
        var settings = builder.Settings.Build();
        services.AddSingleton(settings);

        services.TryAddSingleton(serviceProvider => new PermissionCatalogue(
            serviceProvider.GetRequiredService<SchemaModel>(),
            serviceProvider.GetServices<IPermissionProvider>(),
            settings.Descriptors.Select(descriptor => descriptor.Area)));

        services.AddSingleton<RoleVersionInterceptor>();
        services.AddSingleton<LeaseRenewalInterceptor>();
        services.AddScoped<HookScopeGuard>();

        var contentMigrations = builder.ContentMigrationsAssembly ?? Assembly.GetEntryAssembly() ?? typeof(CmsDbContext).Assembly;
        var pluginModels = plugins.Contributions.Models;
        services.AddDbContext<CmsDbContext>((serviceProvider, options) =>
        {
            // Ahead of the provider's interceptors, so that a refused write has begun no transaction and renewed no lease.
            options.AddInterceptors(serviceProvider.GetRequiredService<HookScopeGuard>());
            ContentMigrations.Configure(options, provider, serviceProvider.GetRequiredService<SchemaModel>(), contentMigrations, pluginModels);
            options.AddInterceptors(serviceProvider.GetRequiredService<RoleVersionInterceptor>());
            LeaseRenewalInterceptor.AddTo(options, serviceProvider, provider);
        });

        services.AddDbContext<SystemDbContext>((serviceProvider, options) =>
        {
            provider.ConfigureSystemMigrations(options);
            options.UseSystemModel(provider.Conventions);
            LeaseRenewalInterceptor.AddTo(options, serviceProvider, provider);
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
        IdentityRegistration.Add(services, builder.Users, builder.ApiKeys, builder.PreviewTokens, builder.Clients, builder.Sso, builder.Members, builder.Providers);
        services.TryAddScoped<IAuthorizationEvaluator, AuthorizationEvaluator>();
        services.AddScoped<ManagementAuthority>();
        services.TryAddScoped<IPermissionChecker, PermissionChecker>();
        services.TryAddScoped<IEffectiveAccess, EffectiveAccess>();
        services.AddScoped<CallerAccess>();
        services.AddScoped<RoleService>();
        services.AddScoped<SuperAdminGuard>();
        services.AddHostedService<DefaultGrantStartupService>();
        services.TryAddScoped<IContentQueryAccess, ContentQueryAccess>();
        services.TryAddScoped<IContentSurface, ContentSurface>();
        services.AddScoped<ReferenceResolvers>();
        services.AddHostedService<ReferenceResolversStartupCheck>();
        services.AddScoped<ManagementShape>();
        services.AddScoped<TargetTypes>();
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
        services.TryAddScoped<ISiteAdministration, SiteAdministration>();
        services.AddScoped<RequestBridge>();
        services.AddDeliveryApi(builder.DeliveryCache);
        services.AddManagementContentApi();
        services.AddManagementIdentityApi();
        services.AddManagementMembersApi();
        services.AddMembersApi();
        services.AddManagementSiteApi();
        services.AddManagementOperationsApi();
        services.AddManagementPluginApi();
        services.AddScoped<SchemaIntrospection>();
        services.TryAddScoped<ISchemaIntrospection>(serviceProvider => serviceProvider.GetRequiredService<SchemaIntrospection>());
        services.AddManagementSchemaApi();
        services.AddManagementTransferApi();
        services.AddFluentCmsHealth();
        services.AddSystemApi();
        services.AddFluentCmsOpenApi(builder.OpenApi);

        services.AddSingleton<SettingsCache>();
        services.AddSingleton<SettingsProtection>();
        services.AddSingleton<FluentCMS.Diagnostics.IInsecureDefaultSource, SecretSettingsReport>();
        services.AddScoped<SettingsService>();
        services.AddScoped<SecretSettingsReprotection>();
        services.TryAddScoped(typeof(ISettings<>), typeof(Settings<>));

        services.AddFluentCmsOptions(RichTextOptions.Section, HostErrorCodes.InvalidOption, new RichTextOptions(), richText => richText.Validate());
        services.TryAddSingleton<IRichTextRenderer, RichTextRenderer>();
        services.TryAddSingleton<ContentEntryMapper>();
        services.TryAddSingleton<IHookEntryMapper>(serviceProvider => serviceProvider.GetRequiredService<ContentEntryMapper>());
        services.TryAddSingleton<EntryCanonicalizer>();
        services.TryAddSingleton<EntryComposer>();
        services.TryAddSingleton<ResponseRichText>();
        services.AddFluentCmsOptions(ContentWriteOptions.Section, HostErrorCodes.InvalidOption, new ContentWriteOptions(), content => content.Validate());
        services.AddScoped<HookPipeline>();
        services.AddScoped<ValidationPipeline>();
        services.AddScoped<EntryStore>();
        services.AddScoped<ScheduleStore>();
        services.AddScoped<ContentService>();
        services.AddScoped<TransferImportMode>();
        services.AddScoped<EntryExport>();
        services.AddFluentCmsOptions(TransferOptions.Section, HostErrorCodes.InvalidOption, new TransferOptions(), transfer => transfer.Validate());
        services.AddSingleton(new TransferContributorRegistrations(plugins.Contributions.TransferContributors));
        services.AddScoped<TransferContributors>();
        services.AddScoped<TransferExporter>();
        services.AddScoped<TransferImporter>();
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

        builder.Jobs.AddJob<TrashPurgeJob, TrashPurgePayload>(TrashPurgeJob.Type, TrashPurgeJob.AttemptTimeout);
        builder.Jobs.AddJob<ScheduledPublishJob, ScheduledPayload>(ScheduledPublishJob.Type, ScheduledChangeTimeout);
        builder.Jobs.AddJob<ScheduledUnpublishJob, ScheduledPayload>(ScheduledUnpublishJob.Type, ScheduledChangeTimeout);
        builder.Events.AddHandler<JobDeadLetteredEvent, ScheduleDeadLetterHandler>();
        builder.Events.AddHandler<SiteUpdatedEvent, SiteCacheInvalidation>();
        services.AddHostedService<TrashPurgeScheduleService>();

        builder.Jobs.AddJob<JobRetentionJob, JobRetentionPayload>(JobRetentionJob.Type, JobRetentionJob.AttemptTimeout);
        services.AddHostedService<JobRetentionScheduleService>();

        builder.Jobs.AddJob<OidcPruneJob, OidcPrunePayload>(OidcPruneJob.Type, OidcPruneJob.AttemptTimeout);
        services.AddHostedService<OidcPruneScheduleService>();
        builder.Jobs.AddJob<OidcKeysJob, OidcKeysPayload>(OidcKeysJob.Type, OidcKeysJob.AttemptTimeout);
        services.AddHostedService<OidcKeysScheduleService>();
        builder.Jobs.AddJob<UnverifiedMembersJob, UnverifiedMembersPayload>(UnverifiedMembersJob.Type, UnverifiedMembersJob.AttemptTimeout);
        builder.Jobs.Options.Queues.TryAdd(AccountMail.Queue, AccountMail.Workers);
        builder.Jobs.AddJob<MemberMailJob, MemberMailRequest>(MemberMailJob.Type, AccountMail.AttemptTimeout);
        builder.Jobs.AddJob<PasswordResetMailJob, PasswordResetMailRequest>(PasswordResetMailJob.Type, AccountMail.AttemptTimeout);

        services.AddFluentCmsOptions(IdempotencyOptions.Section, HostErrorCodes.InvalidOption, builder.Idempotency, idempotency => idempotency.Validate());
        services.AddSingleton<IdempotencyStore>();
        builder.Jobs.AddJob<IdempotencyRetentionJob, IdempotencyRetentionPayload>(IdempotencyRetentionJob.Type, IdempotencyRetentionJob.AttemptTimeout);
        services.AddHostedService<IdempotencyRetentionScheduleService>();

        builder.Events.Outbox.Validate();
        builder.Jobs.Options.Queues.TryAdd(OutboxDrainJob.Queue, 1);
        // No attempt timeout: a drain runs for as long as rows are due, and cuts off each delivery itself at DeliveryTimeout,
        // on a thread of its own, so no handler holds it. A limit would only end a long drain that the sweep starts again.
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
        builder.Jobs.AddJob<ActivityRetentionJob, ActivityRetentionPayload>(ActivityRetentionJob.Type, ActivityRetentionJob.AttemptTimeout);
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
        OutputCacheEviction.Register(builder.Events, () => builder.DeliveryCache.Invalidates);

        services.AddSingleton(builder.Seeding);
        services.AddScoped<SeedStateStore>();
        services.AddScoped<SeedRunner>();
        services.AddScoped<ISeedRunner>(provider => provider.GetRequiredService<SeedRunner>());
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
