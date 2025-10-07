# FluentCMS.Configuration

A powerful database-backed configuration system for .NET applications that enables dynamic configuration management with automatic persistence and real-time updates.

## Overview

FluentCMS.Configuration extends the standard .NET configuration system by providing a database-backed configuration provider that can store, retrieve, and dynamically update configuration values at runtime. This is particularly useful for applications that need to modify configuration settings without restarting the application or updating config files.

## Features

- **Database-Backed Configuration**: Store configuration values in a database using Entity Framework Core
- **SQLite Support**: Built-in support for SQLite with easy extensibility for other databases
- **Automatic Seeding**: Automatically seed database with values from `appsettings.json` on first run
- **Real-time Updates**: Optional automatic reloading of configuration changes
- **Type-Safe Configuration**: Full support for strongly-typed configuration objects
- **Caching**: In-memory caching for improved performance
- **JSON Serialization**: Complex objects stored as JSON in the database
- **Registry System**: Automatic discovery of configuration sections through registration
- **Service Integration**: Access the configuration provider through dependency injection

## Installation

Add the package reference to your project:

```xml
<PackageReference Include="FluentCMS.Configuration" Version="1.0.0" />
```

## Quick Start

### 1. Define Your Configuration Classes

```csharp
public class EmailSettings
{
    public string SmtpServer { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
}

public class LoggingSettings
{
    public string LogLevel { get; set; } = "Information";
    public bool EnableFileLogging { get; set; } = false;
    public string LogFilePath { get; set; } = "./logs/app.log";
}
```

### 2. Register Configuration Sections and Provider

In your `Program.cs`:

```csharp
using FluentCMS.Configuration;
using FluentCMS.Configuration.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Step 1: Register configuration sections for database storage
builder.Services.AddDatabaseOptions<EmailSettings>("EmailSettings", builder.Configuration);
builder.Services.AddDatabaseOptions<LoggingSettings>("LoggingSettings", builder.Configuration);

// Step 2: Add database configuration provider to configuration builder
builder.Configuration.AddDatabaseConfiguration(
    connectionString: "Data Source=config.db",
    reloadInterval: TimeSpan.FromMinutes(5) // Optional: auto-reload every 5 minutes
);

// Step 3: Register the provider as a service for dependency injection
builder.Services.AddDatabaseConfigurationServices(builder.Configuration);

var app = builder.Build();
```

### 3. Use Configuration in Your Services

#### Option A: Using IOptions Pattern (Recommended)

```csharp
[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly IOptions<EmailSettings> _emailSettings;
    
    public ConfigController(IOptions<EmailSettings> emailSettings)
    {
        _emailSettings = emailSettings;
    }

    [HttpGet("email")]
    public EmailSettings GetEmailSettings()
    {
        return _emailSettings.Value;
    }
}
```

#### Option B: Direct Provider Access for Runtime Updates

```csharp
[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly DatabaseConfigurationProvider _configProvider;

    public ConfigController(DatabaseConfigurationProvider configProvider)
    {
        _configProvider = configProvider;
    }

    [HttpGet("email")]
    public async Task<EmailSettings?> GetEmailSettings()
    {
        return await _configProvider.GetConfigurationAsync<EmailSettings>("EmailSettings");
    }

    [HttpPost("email")]
    public async Task<IActionResult> UpdateEmailSettings([FromBody] EmailSettings settings)
    {
        await _configProvider.UpdateConfigurationAsync("EmailSettings", settings);
        return Ok();
    }
}
```

## Advanced Usage

### Custom Database Provider

You can use any Entity Framework Core supported database:

```csharp
var dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
    .UseSqlServer(connectionString)
    .Options;

builder.Configuration.AddDatabaseConfiguration(dbOptions, TimeSpan.FromMinutes(10));
```

### Validation Support

Add validation to your configuration options:

```csharp
builder.Services.AddDatabaseOptions<EmailSettings>("EmailSettings", builder.Configuration)
    .ValidateDataAnnotations()
    .Validate(settings => !string.IsNullOrEmpty(settings.SmtpServer), "SMTP Server is required");
```

### Service Integration for Configuration Management

Create a dedicated service for configuration management:

```csharp
public interface IConfigurationService
{
    Task<T?> GetConfigurationAsync<T>(string section) where T : class;
    Task UpdateConfigurationAsync<T>(string section, T configuration) where T : class;
}

public class ConfigurationService : IConfigurationService
{
    private readonly DatabaseConfigurationProvider _provider;

    public ConfigurationService(DatabaseConfigurationProvider provider)
    {
        _provider = provider;
    }

    public async Task<T?> GetConfigurationAsync<T>(string section) where T : class
    {
        return await _provider.GetConfigurationAsync<T>(section);
    }

    public async Task UpdateConfigurationAsync<T>(string section, T configuration) where T : class
    {
        await _provider.UpdateConfigurationAsync(section, configuration);
    }
}

// Register the service
builder.Services.AddScoped<IConfigurationService, ConfigurationService>();
```

## Why Manual Instantiation in Build()?\

The `DatabaseConfigurationSource.Build()` method manually instantiates the `DatabaseConfigurationProvider`, which is the **correct and standard pattern** for .NET configuration providers. Here's why:

1. **Bootstrap Timing**: Configuration providers are instantiated during the early bootstrap phase, before the DI container is available
2. **Standard Pattern**: All built-in .NET configuration providers (JSON, XML, Environment Variables) use this same approach
3. **Service Access**: The `AddDatabaseConfigurationServices()` extension method registers the provider instance for dependency injection after configuration is built

## Configuration Structure

The system stores configuration in a simple database table:

| Column | Type | Description |
|--------|------|-------------|
| Id | Guid | Primary key |
| Section | String | Configuration section name (e.g., "EmailSettings") |
| Value | String | JSON serialized configuration object |
| Type | String | Full type name of the configuration object |
| CreatedAt | DateTime | Creation timestamp |
| UpdatedAt | DateTime | Last update timestamp |

## How It Works

1. **Registration**: Configuration sections are registered via `AddDatabaseOptions<T>()`
2. **Discovery**: The database provider automatically discovers all registered sections
3. **Provider Creation**: The configuration source manually creates the provider instance (standard .NET pattern)
4. **Seeding**: On first run, values from `appsettings.json` are automatically seeded to the database
5. **Loading**: Configuration values are loaded from the database and integrated into the .NET configuration system
6. **Service Registration**: The provider instance is registered with DI for runtime access
7. **Caching**: Values are cached in memory for performance
8. **Updates**: Runtime updates automatically refresh the configuration and notify consumers

## Best Practices

- **Use strongly-typed configuration classes** for type safety and IntelliSense support
- **Register sections early** in your application startup before adding the database configuration provider
- **Call AddDatabaseConfigurationServices()** after adding the database configuration provider to enable DI access
- **Consider reload intervals** based on your application's needs (frequent changes vs. performance)
- **Use validation** to ensure configuration integrity
- **Handle configuration errors gracefully** in case of database connectivity issues
- **Use IOptions pattern** for most configuration access scenarios
- **Use direct provider access** only when you need to update configuration at runtime

## Dependencies

- **.NET 9.0**: Target framework
- **Microsoft.EntityFrameworkCore**: Core Entity Framework functionality
- **Microsoft.EntityFrameworkCore.Sqlite**: SQLite database provider
- **Microsoft.Extensions.Configuration**: .NET configuration abstractions
- **FluentCMS.Configuration.Abstractions**: Configuration abstractions and registry

## Thread Safety

The configuration provider is thread-safe and can be safely used in multi-threaded applications. The internal cache uses `ConcurrentDictionary` for thread-safe operations.

## Performance Considerations

- Configuration values are cached in memory after first load
- Database queries only occur during initial load and reload intervals
- JSON serialization/deserialization overhead is minimal for most configuration objects
- Consider appropriate cache expiration times based on your update frequency needs

## Contributing

This project is part of FluentCMS. Please refer to the main FluentCMS repository for contribution guidelines.

## License

This project is licensed under the same license as FluentCMS. Please refer to the main repository for license details.
