using FluentCMS.Api;
using FluentCMS.EventBus.InMemory;
using FluentCMS.Plugins;
using FluentCMS.Plugins.TodoManager.Repositories;
using FluentCMS.Providers;
using FluentCMS.Providers.Repositories.EntityFramework;
using FluentCMS.Repositories.DataInitialization;
using FluentCMS.Repositories.EntityFramework.Configuration;
using FluentCMS.Repositories.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/myapp-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

var loggerFactory = new SerilogLoggerFactory(Log.Logger);


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

var services = builder.Services;

builder.Services.AddDatabaseManager(options =>
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

services.AddProviders(options =>
    {
        options.AssemblyPrefixesToScan.Add("FluentCMS");
        options.IgnoreExceptions = false; // Set to true to ignore exceptions during provider loading
    }).UseEntityFramework();

// Add plugin system
builder.AddPlugins(["FluentCMS"], loggerFactory);

// Register providers
services.AddEventPublisher();

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
