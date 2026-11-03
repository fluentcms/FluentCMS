using System.Reflection;
using FluentCMS.Activity;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Content.Validation;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Localization;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Localization;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Core.DependencyInjection;

public static class FluentCmsServiceCollectionExtensions
{
    public static IServiceCollection AddFluentCms(this IServiceCollection services, Action<FluentCmsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new FluentCmsBuilder(services);
        configure(builder);

        var provider = builder.DataProvider ?? throw new FluentCmsConfigurationException(
            StorageErrorCodes.NoDataProvider,
            "AddFluentCms has no data provider. Add the package for your database and call cms.UseSqlite(...), cms.UsePostgreSql(...) or cms.UseSqlServer(...) inside AddFluentCms.");

        services.AddSingleton(provider);
        services.AddSingleton(provider.Conventions);
        services.AddSingleton(provider.JsonDialect);
        services.AddSingleton(provider.JobClaimDialect);

        // Until content types can be added to the builder, a schema comes from the container, and none means system tables only.
        services.TryAddSingleton(_ => new SchemaBuilder().Build());

        services.TryAddSingleton(serviceProvider => new PermissionCatalogue(
            serviceProvider.GetRequiredService<SchemaModel>(),
            serviceProvider.GetServices<IPermissionProvider>()));

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
        services.AddSingleton<MigrationRunner>();
        services.AddHostedService<MigrationStartupService>();

        services.TryAddSingleton<ContentQueryOptions>();
        services.TryAddSingleton<IOperationContextAccessor, OperationContextAccessor>();
        services.TryAddSingleton<IPrincipalRoles, ClaimPrincipalRoles>();
        services.TryAddSingleton<EffectiveGrantCache>();
        services.AddSingleton(serviceProvider => new RowRuleRegistry(
            serviceProvider.GetRequiredService<SchemaModel>(),
            serviceProvider.GetRequiredService<ContentQueryOptions>(),
            builder.Authorization.Rules));
        services.AddHostedService<RowRuleStartupCheck>();
        services.TryAddScoped<IAuthorizationEvaluator, AuthorizationEvaluator>();
        services.TryAddScoped<IContentQueryAccess, ContentQueryAccess>();
        services.AddScoped<ContentQueryEngine>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ISiteResolver, SiteResolver>();
        services.TryAddScoped<ILocaleResolver, LocaleResolver>();
        services.AddScoped<LocaleRegistry>();
        services.AddScoped<LocaleFallbackReader>();
        services.AddScoped<SiteManager>();

        services.TryAddSingleton<IRichTextRenderer, RichTextRenderer>();
        services.TryAddSingleton<ContentEntryMapper>();
        services.TryAddSingleton<IHookEntryMapper>(serviceProvider => serviceProvider.GetRequiredService<ContentEntryMapper>());
        services.TryAddSingleton<EntryCanonicalizer>();
        services.TryAddSingleton<EntryComposer>();
        services.TryAddSingleton<ResponseRichText>();
        services.AddScoped<HookPipeline>();
        services.AddScoped<ValidationPipeline>();
        services.AddScoped<EntryStore>();
        services.AddScoped<ContentService>();
        services.TryAddScoped<IContentService>(serviceProvider => serviceProvider.GetRequiredService<ContentService>());
        services.TryAddScoped(typeof(IContentService<>), typeof(TypedContentService<>));

        services.AddSingleton(builder.Jobs.Options);
        services.AddSingleton<JobRegistry>();
        services.AddSingleton<JobStore>();
        services.AddSingleton<JobExecutor>();
        services.AddSingleton<RecurringJobs>();
        services.TryAddSingleton<IJobEventSink, BusJobEventSink>();
        services.TryAddSingleton<IJobScheduler, JobScheduler>();
        services.TryAddSingleton<IJobRunner, JobRunner>();
        services.TryAddSingleton<IJobDeadLetters, JobDeadLetters>();
        services.AddHostedService<JobRunnerHostedService>();

        builder.Jobs.AddJob<TrashPurgeJob, TrashPurgePayload>(TrashPurgeJob.Type);
        services.AddHostedService<TrashPurgeScheduleService>();

        builder.Jobs.AddJob<JobRetentionJob, JobRetentionPayload>(JobRetentionJob.Type, JobRetentionJob.AttemptTimeout);
        services.AddHostedService<JobRetentionScheduleService>();

        builder.Events.Outbox.Validate();
        builder.Jobs.Options.Queues.TryAdd(OutboxDrainJob.Queue, 1);
        builder.Jobs.AddJob<OutboxDrainJob, OutboxDrainPayload>(OutboxDrainJob.Type);
        services.AddSingleton(builder.Events.Outbox);
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
        services.AddSingleton(builder.Activity);
        services.AddScoped<ActivityLogWriter>();
        services.TryAddScoped<IActivityLog>(serviceProvider => serviceProvider.GetRequiredService<ActivityLogWriter>());
        services.TryAddScoped<IActivityQuery, ActivityQuery>();
        ActivityEvents.Register(builder.Events);
        services.AddHostedService<ActivityScheduleService>();

        return services;
    }
}
