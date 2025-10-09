using FluentCMS.Api;
using FluentCMS.Infrastructure.Configuration.EntityFramework;
using FluentCMS.Infrastructure.Configuration.EntityFramework.Sqlite;
using FluentCMS.Infrastructure.EventBus.InMemory;
using FluentCMS.Infrastructure.Logging;
using FluentCMS.Infrastructure.Repositories;
using FluentCMS.Infrastructure.Repositories.EntityFramework.Configuration;
using FluentCMS.Infrastructure.Repositories.EntityFramework.Sqlite;
using FluentCMS.Plugins;
using FluentCMS.Plugins.TodoManager.Repositories;
using FluentCMS.Providers;
using FluentCMS.Providers.Repositories.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddSqliteConfiguration("DefaultConnection", TimeSpan.FromMinutes(5));

var loggerFactory = builder.Host.InitiLogFactory();

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

services.AddProviders(options =>
    {
        options.AssemblyPrefixesToScan.Add("FluentCMS");
        options.IgnoreExceptions = false; // Set to true to ignore exceptions during provider loading
    }).UseEntityFramework();

// Add plugin system
builder.AddPlugins(options =>
{
    options.PluginPrefixes = ["FluentCMS"];
    options.LoggerFactory = loggerFactory;
});

// Register providers
services.AddInMemoryEventBus();

// Add services to the container.
services.AddFluentCmsApi();

var app = builder.Build();

app.UseFluentCmsApi();

// Use plugin system
app.UsePlugins();

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
