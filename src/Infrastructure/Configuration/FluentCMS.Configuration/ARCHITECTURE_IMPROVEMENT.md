# Database Configuration Provider - Improved Architecture

## Overview

The configuration seeding logic has been moved from the `DatabaseConfigurationProvider.Load()` method to a dedicated `ConfigurationSeedingHostedService`. This improves the architecture by:

1. **Separation of Concerns**: Configuration providers focus on reading configuration, not data initialization
2. **Performance**: The Load() method is called frequently; seeding only happens once at startup
3. **Reliability**: Hosted services provide better error handling and lifecycle management
4. **Testability**: Easier to test configuration loading separately from seeding logic

## Usage

### Basic Setup

```csharp
// In Program.cs or Startup.cs

// 1. Configure the database options
var dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
    .UseSqlite("Data Source=config.db")
    .Options;

// 2. Add database configuration provider to read from database
builder.Configuration.AddDatabaseConfiguration(dbOptions, TimeSpan.FromMinutes(5));

// 3. Build the configuration
var configuration = builder.Configuration.Build();

// 4. Add seeding hosted service to populate database from appsettings.json
builder.Services.AddDatabaseConfigurationSeeding(
    dbOptions,
    seedConfiguration: configuration, // Use built configuration for seeding
    dynamicSections: new List<string> { "Logging", "MyCustomSection", "ApiSettings" }
);

// 5. Register provider as service for runtime updates (optional)
builder.Services.AddDatabaseConfigurationServices(configuration);
```

### Advanced Usage with Automatic Section Discovery

```csharp
// If using the DatabaseConfigurationRegistry for section registration
// (this would be done during service registration)

// Register sections to be managed in database
DatabaseConfigurationRegistry.RegisterSection<LoggingOptions>("Logging");
DatabaseConfigurationRegistry.RegisterSection<MyCustomOptions>("MyCustomSection");

// The seeding service automatically discovers registered sections
builder.Services.AddDatabaseConfigurationSeeding(
    dbOptions,
    seedConfiguration: configuration
    // dynamicSections will be auto-discovered from registry
);
```

### Runtime Configuration Updates

```csharp
// In a controller or service
public class ConfigurationController : ControllerBase
{
    private readonly DatabaseConfigurationProvider _configProvider;

    public ConfigurationController(DatabaseConfigurationProvider configProvider)
    {
        _configProvider = configProvider;
    }

    [HttpPost("update-logging")]
    public async Task<IActionResult> UpdateLogging([FromBody] LoggingConfiguration config)
    {
        await _configProvider.UpdateConfigurationAsync("Logging", config);
        return Ok();
    }

    [HttpGet("logging")]
    public async Task<IActionResult> GetLogging()
    {
        var config = await _configProvider.GetConfigurationAsync<LoggingConfiguration>("Logging");
        return Ok(config);
    }
}
```

## Architecture Benefits

### Before (Problematic)
```
Load() Method:
??? EnsureCreated()
??? SeedDynamicSections() ? Heavy operation on every load
??? LoadConfigurationsFromDatabase()
??? SetupReloadTimer()
```

### After (Improved)
```
Startup:
??? ConfigurationSeedingHostedService.StartAsync()
?   ??? EnsureCreated()
?   ??? SeedDynamicSections() ? One-time operation
??? DatabaseConfigurationProvider.Load()
    ??? LoadConfigurationsFromDatabase() ? Lightweight
    ??? SetupReloadTimer()
```

## Key Changes

1. **Removed from DatabaseConfigurationProvider**:
   - `SeedDynamicSections()` method
   - `GetSectionAsDictionary()` method
   - Constructor parameters for `seedConfiguration` and `dynamicSections`

2. **Added ConfigurationSeedingHostedService**:
   - Handles all seeding logic
   - Runs once at application startup
   - Proper error handling with logging
   - Configurable via `ConfigurationSeedingOptions`

3. **Updated Extension Methods**:
   - Simplified `AddDatabaseConfiguration()`
   - Added `AddDatabaseConfigurationSeeding()`
   - Improved service registration

This architecture is more maintainable, performant, and follows ASP.NET Core best practices for background operations.
