# FluentCMS.Database.Extensions

Configuration and dependency injection extensions for FluentCMS database management with library-based multi-database support.

## Overview

This package provides the configuration layer and dependency injection extensions for FluentCMS database system. It enables fluent configuration of multiple database providers with library-based markers, allowing different parts of your application to use different databases while maintaining type safety and high performance.

## Key Features

- **Fluent Configuration API** for mapping libraries to database providers
- **High-Performance Resolution** with compile-time dependency injection
- **Library-Based Mapping** using marker interfaces
- **Default Database Support** for services without specific requirements
- **Extensible Provider System** supporting multiple database backends
- **Type-Safe Registration** with generic constraints

## Core Components

### ServiceCollectionExtensions

Extension methods for configuring database providers in your DI container:

```csharp
public static IServiceCollection AddDatabaseManager(
    this IServiceCollection services, 
    Action<ILibraryDatabaseMappingBuilder> configureMapping)
```

### Configuration Interfaces

**ILibraryDatabaseMappingBuilder**: Main configuration entry point
```csharp
public interface ILibraryDatabaseMappingBuilder
{
    ILibraryMappingBuilder SetDefault();
    ILibraryMappingBuilder MapLibrary<TLibraryMarker>() where TLibraryMarker : IDatabaseManagerMarker;
}
```

**ILibraryMappingBuilder**: Provider configuration for specific mappings
```csharp
public interface ILibraryMappingBuilder
{
    ILibraryMappingBuilder RegisterProvider(string providerName, string connectionString, 
        Func<string, IServiceProvider, IDatabaseManager> factory);
}
```

### IDefaultLibraryMarker

Built-in marker for services that should use the default database configuration:

```csharp
public interface IDefaultLibraryMarker : IDatabaseManagerMarker
{
    // Empty interface - serves purely as a marker for default database configuration
}
```

## Configuration Examples

### Basic Setup with Default Database

```csharp
using FluentCMS.Database.Extensions;
using FluentCMS.Database.SqlServer;

// In Program.cs or Startup.cs
services.AddDatabaseManager(options =>
{
    // Set default database that all services will use unless specifically mapped
    options.SetDefault().UseSqlServer("Server=.;Database=DefaultCMS;Integrated Security=true;");
});

// Services using default database
public class GeneralService
{
    public GeneralService(IDatabaseManager<IDefaultLibraryMarker> databaseManager) { }
}
```

### Multi-Database Configuration

```csharp
using FluentCMS.Database.Extensions;
using FluentCMS.Database.SqlServer;
using FluentCMS.Database.Sqlite;

// Define library markers
public interface IContentLibraryMarker : IDatabaseManagerMarker { }
public interface IUserLibraryMarker : IDatabaseManagerMarker { }
public interface IAnalyticsLibraryMarker : IDatabaseManagerMarker { }

// Configure multiple databases
services.AddDatabaseManager(options =>
{
    // Default database (SQL Server)
    options.SetDefault()
           .UseSqlServer("Server=.;Database=DefaultCMS;Integrated Security=true;");
    
    // Content management on dedicated SQL Server
    options.MapLibrary<IContentLibraryMarker>()
           .UseSqlServer("Server=content-db;Database=ContentCMS;Integrated Security=true;");
    
    // User data on separate SQL Server with different credentials
    options.MapLibrary<IUserLibraryMarker>()
           .UseSqlServer("Server=user-db;Database=UserCMS;User Id=cms_user;Password=secure123;");
    
    // Analytics on SQLite for fast local storage
    options.MapLibrary<IAnalyticsLibraryMarker>()
           .UseSqlite("Data Source=analytics.db;");
});
```

### Environment-Specific Configuration

```csharp
services.AddDatabaseManager(options =>
{
    if (Environment.IsDevelopment())
    {
        // Development: Use SQLite for simplicity
        options.SetDefault()
               .UseSqlite("Data Source=development.db;");
        
        options.MapLibrary<IContentLibraryMarker>()
               .UseSqlite("Data Source=content_dev.db;");
    }
    else
    {
        // Production: Use SQL Server with connection strings from configuration
        var defaultConnString = configuration.GetConnectionString("DefaultDatabase");
        var contentConnString = configuration.GetConnectionString("ContentDatabase");
        
        options.SetDefault()
               .UseSqlServer(defaultConnString);
        
        options.MapLibrary<IContentLibraryMarker>()
               .UseSqlServer(contentConnString);
    }
});
```

## Service Usage

### Injecting Database Managers

```csharp
// Default database
public class DefaultService
{
    private readonly IDatabaseManager<IDefaultLibraryMarker> _db;
    
    public DefaultService(IDatabaseManager<IDefaultLibraryMarker> databaseManager)
    {
        _db = databaseManager;
    }
}

// Library-specific database
public class ContentService
{
    private readonly IDatabaseManager<IContentLibraryMarker> _db;
    
    public ContentService(IDatabaseManager<IContentLibraryMarker> databaseManager)
    {
        _db = databaseManager;
    }
    
    public async Task InitializeContent(CancellationToken cancellationToken = default)
    {
        // Each service operates on its configured database
        if (!await _db.DatabaseExists(cancellationToken))
        {
            await _db.CreateDatabase(cancellationToken);
        }
        
        var tables = new[] { "Contents", "Categories", "Tags" };
        if (!await _db.TablesExist(tables, cancellationToken))
        {
            await _db.ExecuteRaw(contentSchemaScript, cancellationToken);
        }
    }
}
```

## Advanced Scenarios

### Custom Database Provider

```csharp
// Create extension method for your custom provider
public static class CustomDatabaseExtensions
{
    public static ILibraryMappingBuilder UseCustomDatabase(
        this ILibraryMappingBuilder builder, 
        string connectionString)
    {
        return builder.RegisterProvider("CustomDB", connectionString, (connString, serviceProvider) =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<CustomDatabaseManager>>();
            return new CustomDatabaseManager(connString, logger);
        });
    }
}

// Use in configuration
services.AddDatabaseManager(options =>
{
    options.MapLibrary<ISpecialLibraryMarker>()
           .UseCustomDatabase("CustomConnectionString");
});
```

### Conditional Mapping

```csharp
services.AddDatabaseManager(options =>
{
    options.SetDefault().UseSqlServer(defaultConnection);
    
    // Conditionally map libraries based on feature flags
    if (featureFlags.IsAnalyticsEnabled)
    {
        options.MapLibrary<IAnalyticsLibraryMarker>()
               .UseSqlite("Data Source=analytics.db;");
    }
    
    if (featureFlags.IsMultiTenantEnabled)
    {
        options.MapLibrary<ITenantLibraryMarker>()
               .UseSqlServer(tenantConnection);
    }
});
```

## Architecture Benefits

1. **Compile-Time Resolution**: No runtime reflection or service location
2. **Type Safety**: Generic constraints prevent incorrect usage
3. **Performance**: Direct dependency injection with minimal overhead
4. **Testability**: Easy to mock specific database contexts
5. **Modularity**: Each library can have isolated database configuration
6. **Scalability**: Add new providers without changing existing code

## Validation

The system includes built-in validation:

- **Default Configuration Required**: At least one default configuration must be provided
- **Connection String Validation**: Ensures connection strings are not null or empty
- **Provider Name Validation**: Ensures provider names are properly specified
- **Generic Constraints**: Compile-time validation of marker interfaces

## Dependencies

This package depends on:
- **FluentCMS.Database.Abstractions**: Core interfaces and contracts
- **Microsoft.Extensions.DependencyInjection**: Dependency injection container integration

## Target Framework

- **.NET 9.0**

## Related Packages

- **FluentCMS.Database.Abstractions**: Core interfaces and abstractions
- **FluentCMS.Database.SqlServer**: SQL Server provider implementation  
- **FluentCMS.Database.Sqlite**: SQLite provider implementation
