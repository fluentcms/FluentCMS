using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddFluentCmsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddDbContext<FluentCmsDbContext>((provider, options) =>
        {
            var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            switch (database.ResolveProvider())
            {
                case DatabaseProviders.Sqlite:
                    options.UseSqlite(database.ConnectionString);
                    break;
                case DatabaseProviders.PostgreSql:
                    options.UseNpgsql(database.ConnectionString);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown database provider '{database.Provider}'. Use '{DatabaseProviders.Sqlite}' or '{DatabaseProviders.PostgreSql}'.");
            }
        });

        services.AddHostedService<DatabaseMigrator>();

        return services;
    }
}
