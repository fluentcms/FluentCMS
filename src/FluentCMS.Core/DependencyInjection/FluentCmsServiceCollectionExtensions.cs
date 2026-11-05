using System.Reflection;
using FluentCMS.Activity;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Api.Delivery;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Events;
using FluentCMS.Core.Health;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Localization;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Seeding;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Localization;
using FluentCMS.Schema;
using FluentCMS.Seeding;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.AddProductionStartupChecks();
        services.AddFluentCmsOptions(DatabaseOptions.Section, StorageErrorCodes.InvalidDatabaseSetting, new DatabaseOptions());
        services.AddFluentCmsOptions(SecurityOptions.Section, HostErrorCodes.InvalidOption, new SecurityOptions(), security => security.Validate());
        services.AddFluentCmsOptions(ReverseProxyOptions.Section, HostErrorCodes.InvalidOption, builder.ReverseProxy, proxy => proxy.Validate());
        services.AddReverseProxyHttpsPolicy();
        services.AddSecurityDefaults();
        services.AddFluentCmsApiProtection();

        services.AddSingleton(provider);
        services.AddSingleton(provider.Conventions);
        services.AddSingleton(provider.JsonDialect);
        services.AddSingleton(provider.JobClaimDialect);

        // Until content types can be added to the builder, a schema comes from the container, and none means system tables only.
        services.TryAddSingleton(_ => new SchemaBuilder().Build());

        builder.Seeding.Validate();
        if (builder.Seeding.Types.Count > 0)
        {
            builder.Settings.Add<SeedingState>();
        }

        var settings = builder.Settings.Build();
        services.AddSingleton(settings);

        services.TryAddSingleton(serviceProvider => new PermissionCatalogue(
            serviceProvider.GetRequiredService<SchemaModel>(),
            serviceProvider.GetServices<IPermissionProvider>(),
            settings.Descriptors.Select(descriptor => descriptor.Area)));

        services.AddSingleton<RoleVersionInterceptor>();

        var contentMigrations = builder.ContentMigrationsAssembly ?? Assembly.GetEntryAssembly() ?? typeof(CmsDbContext).Assembly;
        services.AddDbContext<CmsDbContext>((serviceProvider, options) =>
        {
            ContentMigrations.Configure(options, provider, serviceProvider.GetRequiredService<SchemaModel>(), contentMigrations);
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
        IdentityRegistration.Add(services, builder.Users);
        services.TryAddScoped<IAuthorizationEvaluator, AuthorizationEvaluator>();
        services.TryAddScoped<IContentQueryAccess, ContentQueryAccess>();
        services.AddScoped<ContentQueryEngine>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ISiteResolver, SiteResolver>();
        services.TryAddScoped<ILocaleResolver, LocaleResolver>();
        services.AddScoped<LocaleRegistry>();
        services.AddScoped<LocaleFallbackReader>();
        services.AddScoped<SiteManager>();
        services.AddScoped<RequestBridge>();
        services.AddDeliveryApi();
        services.AddFluentCmsHealth();
        services.AddSystemApi();

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

        services.AddFluentCmsOptions(JobsSection, JobErrorCodes.InvalidRunnerOptions, builder.Jobs.Options, jobs => jobs.Validate());
        services.AddSingleton<JobRegistry>();
        services.AddSingleton<JobStore>();
        services.AddSingleton<JobExecutor>();
        services.AddSingleton<RecurringJobs>();
        services.AddSingleton<JobFactory>();
        services.TryAddSingleton<IJobEventSink, BusJobEventSink>();
        services.TryAddSingleton<IJobScheduler, JobScheduler>();
        services.TryAddSingleton<IJobRunner, JobRunner>();
        services.TryAddSingleton<IJobDeadLetters, JobDeadLetters>();
        services.AddHostedService<JobRunnerHostedService>();

        builder.Jobs.AddJob<TrashPurgeJob, TrashPurgePayload>(TrashPurgeJob.Type);
        builder.Jobs.AddJob<ScheduledPublishJob, ScheduledPayload>(ScheduledPublishJob.Type);
        builder.Jobs.AddJob<ScheduledUnpublishJob, ScheduledPayload>(ScheduledUnpublishJob.Type);
        builder.Events.AddHandler<JobDeadLetteredEvent, ScheduleDeadLetterHandler>();
        services.AddHostedService<TrashPurgeScheduleService>();

        builder.Jobs.AddJob<JobRetentionJob, JobRetentionPayload>(JobRetentionJob.Type, JobRetentionJob.AttemptTimeout);
        services.AddHostedService<JobRetentionScheduleService>();

        builder.Events.Outbox.Validate();
        builder.Jobs.Options.Queues.TryAdd(OutboxDrainJob.Queue, 1);
        builder.Jobs.AddJob<OutboxDrainJob, OutboxDrainPayload>(OutboxDrainJob.Type);
        services.AddFluentCmsOptions(OutboxSection, EventErrorCodes.InvalidOutboxOptions, builder.Events.Outbox, outbox => outbox.Validate());
        services.AddSingleton<OutboxRegistry>();
        services.AddSingleton<OutboxKick>();
        services.AddSingleton<OutboxRows>();
        services.TryAddSingleton<IEventDeadLetters, OutboxDeadLetters>();
        services.AddSingleton<OutboxDrainer>();
        services.AddHostedService<OutboxScheduleService>();
        services.AddScoped<EventDispatcher>();
        services.AddScoped<OutboxWriter>();
        services.AddScoped<EventBuffer>();
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
        ActivityEvents.Register(builder.Events);
        services.AddHostedService<ActivityScheduleService>();

        services.AddSingleton(builder.Seeding);
        services.AddScoped<SeedStateStore>();
        services.AddScoped<ISeedRunner, SeedRunner>();
        if (builder.Seeding.Types.Count > 0 && builder.Seeding.RunOnStart)
        {
            services.AddHostedService<SeedStartupService>();
        }

        return services;
    }
}
