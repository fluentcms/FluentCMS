# FluentCMS.Database.Abstractions

Core abstractions for FluentCMS database management with library-based multi-database support.

## Overview

This package provides the foundational interfaces for database operations in FluentCMS, enabling type-safe multi-database configurations through library markers. It implements a marker-based pattern that allows different parts of your application to use different database providers while maintaining compile-time type safety.

## Key Features

- **Type-safe database resolution** using library markers
- **Multi-database support** for complex applications
- **Compile-time validation** with generic constraints
- **Clean separation** between database logic and implementation
- **Async/await pattern** throughout all operations

## Core Interfaces

### IDatabaseManager

The primary interface for database operations:

```csharp
public interface IDatabaseManager
{
    Task<bool> DatabaseExists(CancellationToken cancellationToken = default);
    Task CreateDatabase(CancellationToken cancellationToken = default);
    Task<bool> TablesExist(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);
    Task<bool> TablesEmpty(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);
    Task ExecuteRaw(string sql, CancellationToken cancellationToken = default);
}
```

### IDatabaseManager<T>

Generic interface that provides library-based database resolution:

```csharp
public interface IDatabaseManager<T> : IDatabaseManager where T : IDatabaseManagerMarker
{
    // Inherits all IDatabaseManager methods with type safety
}
```

### IDatabaseManagerMarker

Base marker interface for creating library-specific database markers:

```csharp
public interface IDatabaseManagerMarker
{
    // Empty interface - serves purely as a marker and type constraint
}
```

## Usage Examples

### Creating a Library Marker

```csharp
// Define your library marker
public interface IContentLibraryMarker : IDatabaseManagerMarker { }

// Use in your services
public class ContentService
{
    private readonly IDatabaseManager<IContentLibraryMarker> _databaseManager;
    
    public ContentService(IDatabaseManager<IContentLibraryMarker> databaseManager)
    {
        _databaseManager = databaseManager;
    }
    
    public async Task InitializeDatabase(CancellationToken cancellationToken = default)
    {
        if (!await _databaseManager.DatabaseExists(cancellationToken))
        {
            await _databaseManager.CreateDatabase(cancellationToken);
        }
        
        var tableNames = new[] { "Contents", "Categories" };
        if (!await _databaseManager.TablesExist(tableNames, cancellationToken))
        {
            // Create tables using ExecuteRaw
            await _databaseManager.ExecuteRaw(createTablesScript, cancellationToken);
        }
    }
}
```

### Multi-Library Scenario

```csharp
// Content management library
public interface IContentLibraryMarker : IDatabaseManagerMarker { }

// User management library  
public interface IUserLibraryMarker : IDatabaseManagerMarker { }

// Analytics library
public interface IAnalyticsLibraryMarker : IDatabaseManagerMarker { }

// Each service gets its own database configuration
public class ContentService
{
    public ContentService(IDatabaseManager<IContentLibraryMarker> db) { }
}

public class UserService  
{
    public UserService(IDatabaseManager<IUserLibraryMarker> db) { }
}

public class AnalyticsService
{
    public AnalyticsService(IDatabaseManager<IAnalyticsLibraryMarker> db) { }
}
```

## Benefits

1. **Type Safety**: Generic constraints ensure only valid markers can be used
2. **Separation of Concerns**: Each library can have its own database configuration
3. **Testability**: Easy to mock and test individual database contexts
4. **Scalability**: Add new database providers without changing existing code
5. **Performance**: Compile-time resolution eliminates runtime overhead

## Dependencies

- **Microsoft.Extensions.DependencyInjection.Abstractions** (9.0.9)
- **Microsoft.Extensions.Logging.Abstractions** (9.0.9)

## Target Framework

- **.NET 9.0**

## Next Steps

To use this package:

1. Install a database provider package (e.g., `FluentCMS.Database.SqlServer` or `FluentCMS.Database.Sqlite`)
2. Install the extensions package (`FluentCMS.Database.Extensions`)
3. Configure your database mappings in your DI container
4. Create library markers for your different modules
5. Inject `IDatabaseManager<TMarker>` into your services

## Related Packages

- **FluentCMS.Database.Extensions**: Configuration and dependency injection
- **FluentCMS.Database.SqlServer**: SQL Server provider implementation
- **FluentCMS.Database.Sqlite**: SQLite provider implementation
