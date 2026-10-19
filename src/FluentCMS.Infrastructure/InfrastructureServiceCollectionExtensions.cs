using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Content;
using FluentCMS.Core.Delivery;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Media;
using FluentCMS.Core.Models;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Agents;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.ChangeSets;
using FluentCMS.Infrastructure.Checks;
using FluentCMS.Infrastructure.Content;
using FluentCMS.Infrastructure.Delivery;
using FluentCMS.Infrastructure.Entities;
using FluentCMS.Infrastructure.Identity;
using FluentCMS.Infrastructure.Jobs;
using FluentCMS.Infrastructure.Media;
using FluentCMS.Infrastructure.Models;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Infrastructure.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddFluentCmsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        AddDatabase(services, configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions());
        AddIdentity(services);
        AddAudit(services);
        AddSchema(services);
        AddChangeSets(services);
        AddAgents(services);
        AddContent(services, configuration);
        AddMedia(services, configuration);
        AddChecks(services);
        AddJobs(services);
        AddModels(services, configuration);

        services.AddHostedService<DatabaseMigrator>();

        return services;
    }

    // The provider is chosen once at startup; each provider subclass carries its own migrations (ADR-0005).
    private static void AddDatabase(IServiceCollection services, DatabaseOptions database)
    {
        switch (database.ResolveProvider())
        {
            case DatabaseProviders.Sqlite:
                services.AddDbContext<FluentCmsDbContext, SqliteFluentCmsDbContext>(options => options.UseSqlite(database.ConnectionString));
                break;
            case DatabaseProviders.PostgreSql:
                services.AddDbContext<FluentCmsDbContext, PostgreSqlFluentCmsDbContext>(options => options.UseNpgsql(database.ConnectionString));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown database provider '{database.Provider}'. Use '{DatabaseProviders.Sqlite}' or '{DatabaseProviders.PostgreSql}'.");
        }
    }

    // The log reads ICurrentInitiator, which each host registers for its own kind of caller.
    private static void AddAudit(IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditReader, AuditReader>();
    }

    // FieldTypeRegistry comes from AddFluentCmsCore, so built-in and plugin field types reach the directory the same way.
    private static void AddSchema(IServiceCollection services)
    {
        services.AddScoped<IContentTypeDirectory, ContentTypeDirectory>();
    }

    private static void AddChangeSets(IServiceCollection services)
    {
        // The content type directory writes its operations through the same instance (SCH-05).
        services.AddScoped<ChangeSetDirectory>();
        services.AddScoped<IChangeSetDirectory>(provider => provider.GetRequiredService<ChangeSetDirectory>());
        services.AddScoped<IChangeSetLists, ChangeSetLists>();
    }

    private static void AddAgents(IServiceCollection services)
    {
        services.AddScoped<IAgentCredentialDirectory, AgentCredentialDirectory>();
        services.AddScoped<IAgentActivity, AgentActivity>();
        services.AddSingleton<IToolCallLog, ToolCallLog>();
    }

    // The configured storage is checked against the registered ones when the host starts (MED-04).
    private static void AddMedia(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LocalDiskOptions>(configuration.GetSection(LocalDiskOptions.SectionName));
        services.AddFluentCmsExtension<IMediaStorage, LocalDiskMediaStorage>();
        services.AddOptions<MediaOptions>().Bind(configuration.GetSection(MediaOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<MediaOptions>, MediaOptionsValidation>();
        services.AddScoped<IMediaLibrary, MediaLibrary>();
    }

    private static void AddContent(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ContentOptions>(configuration.GetSection(ContentOptions.SectionName));
        services.AddOptions<EntityOptions>().Bind(configuration.GetSection(EntityOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<EntityOptions>, EntityOptionsValidation>();
        services.AddScoped<IEntryDirectory, EntryDirectory>();
        services.AddScoped<IWhereUsed, WhereUsed>();
        services.AddScoped<IResolvedEntries, ResolvedEntries>();
        services.AddScoped<IPublishedContent, PublishedContentReader>();
        services.AddScoped<IEntityRelations, EntityRelationsReader>();
        services.AddScoped<IEntityFacts, EntityFactsReader>();
        services.AddScoped<IBlastRadius, BlastRadiusReader>();
    }

    // CheckTypeRegistry comes from AddFluentCmsCore, so built-in and plugin check types reach the directory the same way.
    private static void AddChecks(IServiceCollection services)
    {
        services.AddScoped<ICheckDirectory, CheckDirectory>();
    }

    // The worker in Host claims and finishes jobs through IJobLeases; everything else enqueues and reads through IJobQueue.
    private static void AddJobs(IServiceCollection services)
    {
        services.AddScoped<IJobQueue, JobQueue>();
        services.AddScoped<IJobLeases, JobLeases>();
    }

    // The provider wiring behind the gateway in Core (ADR-0008). A profile the host cannot use stops it at startup.
    private static void AddModels(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ModelOptions>().Bind(configuration.GetSection(ModelOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ModelOptions>, ModelOptionsValidation>();
        services.Configure<OutboundOptions>(configuration.GetSection(OutboundOptions.SectionName));
        services.TryAddSingleton(ModelTransport.Network);
        services.AddSingleton<IModelProfiles, ModelProfiles>();
        services.AddSingleton<IOutboundHosts, OutboundHosts>();
        services.AddSingleton<IModelClients, ModelClients>();
        services.AddScoped<IModelCallLog, ModelCallLog>();
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddIdentityCore<FluentCmsUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<FluentCmsRole>()
            .AddEntityFrameworkStores<FluentCmsDbContext>()
            .AddClaimsPrincipalFactory<FluentCmsClaimsPrincipalFactory>();

        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IFirstRunSetup, FirstRunSetup>();
    }
}
