using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Identity;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddIdentityCore<FluentCmsUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<FluentCmsRole>()
            .AddEntityFrameworkStores<FluentCmsDbContext>();

        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IFirstRunSetup, FirstRunSetup>();
    }
}
