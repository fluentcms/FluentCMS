using FluentCMS.Repositories.Postgres.Repositories;
using FluentCMS.Repositories.Postgres.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FluentCMS.Repositories.Postgres.Extensions;

public static class PostgresServiceExtensions
{
    public static IServiceCollection AddPostgresDbRepositories(this IServiceCollection services, string connectionStringName)
    {
        services.AddDbContext<PostgresDbContext>((provider, optionsBuilder) =>
        {
            var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");
            var connectionString = configuration.GetConnectionString(connectionStringName);

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.EnableDynamicJson();


            optionsBuilder.UseNpgsql(dataSourceBuilder.Build());
        });



        // Register repositories
       services.Scan(scan => scan
            .FromAssembliesOf(typeof(PostgresDbContext))
            .AddClasses(classes => classes.AssignableTo(typeof(IService)))
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        services.AddHostedService<DatabaseMigrator>();

        return services;
    }


}
