using FluentCMS.Infrastructure;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;
using FluentCMS.Repositories.EntityFramework.Configuration;
using FluentCMS.Repositories.Sqlite;
using FluentCMS.Repositories.Tests.Integration.TestEntities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.Tests.Integration.TestFixtures;

public static class ServiceExtensions
{
    public static IServiceCollection AddTestServices(this IServiceCollection services, string connectionString)
    {
        services.AddDatabaseManager(options =>
        {
            // Default database for most libraries
            options.Default()
                .UseSqlite(connectionString)
                .EnableSchemaValidation(validationOptions =>
                {
                    validationOptions.IgnoreExceptions = false; // Fail fast on errors
                    validationOptions.Conditions.Add(new AlwaysTrueCondition());
                });
        });

        services.AddDatabaseContext<TestDbContext, ITestDatabaseMarker>();
        services.AddSchemaValidator<TestSchemaValidator, ITestDatabaseMarker>();
        services.AddScoped<IApplicationExecutionContext, SystemExecutionContext>();
        services.AddScoped<IRepository<TestUser>, Repository<TestUser, TestDbContext>>();
        services.AddScoped<IRepository<TestProduct>, Repository<TestProduct, TestDbContext>>();
        services.AddScoped<IRepository<TestCategory>, Repository<TestCategory, TestDbContext>>();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        return services;
    }
}
