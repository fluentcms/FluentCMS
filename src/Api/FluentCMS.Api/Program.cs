using FluentCMS.Api.Core.Api;
using FluentCMS.Api.Core.Repositories.EntityFramework;
using FluentCMS.Api.Plugins.TodoManagement.Repositories;
using FluentCMS.Infrastructure.Configuration.EntityFramework;
using FluentCMS.Infrastructure.Configuration.EntityFramework.Sqlite;
using FluentCMS.Infrastructure.EventBus.InMemory;
using FluentCMS.Infrastructure.Logging;
using FluentCMS.Infrastructure.Plugins;
using FluentCMS.Infrastructure.Providers;
using FluentCMS.Infrastructure.Providers.Repositories.EntityFramework;
using FluentCMS.Infrastructure.Repositories;
using FluentCMS.Infrastructure.Repositories.EntityFramework.Configuration;
using FluentCMS.Infrastructure.Repositories.EntityFramework.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddSqliteConfiguration("DefaultConnection", TimeSpan.FromMinutes(5));

var loggerFactory = builder.Host.InitLogFactory();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

var services = builder.Services;

services.AddDatabaseManager(options =>
{
    // Default database for most libraries
    options.Default()
        .UseSqlite(connectionString)
        .EnableDataSeeding(seedingOptions =>
        {
            seedingOptions.IgnoreExceptions = false; // Fail fast on errors
            seedingOptions.Conditions.Add(new EnvironmentCondition(builder.Environment, e => e.IsDevelopment()));
        })
        .EnableSchemaValidation(validationOptions =>
        {
            validationOptions.IgnoreExceptions = false; // Fail fast on errors
            validationOptions.Conditions.Add(new EnvironmentCondition(builder.Environment, e => e.IsDevelopment()));
        });

    // Specific database for ToDo library
    options.For<ITodoDatabaseMarker>()
        .UseSqlite("DataSource=todo.db;Cache=Shared")
        .EnableDataSeeding(seedingOptions =>
        {
            seedingOptions.IgnoreExceptions = false; // Fail fast on errors
            seedingOptions.Conditions.Add(new EnvironmentCondition(builder.Environment, e => e.IsDevelopment()));
        })
        .EnableSchemaValidation(validationOptions =>
        {
            validationOptions.IgnoreExceptions = false; // Fail fast on errors
            validationOptions.Conditions.Add(new EnvironmentCondition(builder.Environment, e => e.IsDevelopment()));
        });
});

services.AddDbConfiguration();

services.AddEntityFrameworkRpositories();

services.AddProviders(options =>
{
    options.AssemblyPrefixesToScan.Add("FluentCMS");
    options.IgnoreExceptions = false; // Set to true to ignore exceptions during provider loading
}).UseEntityFramework();

// Register providers
services.AddInMemoryEventBus();

// Add services to the container.
services.AddFluentCmsApi();

// Add plugin system
services.AddPluginSystem(builder.Configuration, options =>
{
    options.ScanAssemblyPatterns = ["FluentCMS.*"];
    options.LoggerFactory = loggerFactory;
});

var app = builder.Build();

app.UseFluentCmsApi();

// Use plugin system
app.UsePluginSystem();

try
{
    Log.Information("Starting web application");
    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
