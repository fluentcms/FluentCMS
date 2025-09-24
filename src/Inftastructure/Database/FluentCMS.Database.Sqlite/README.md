# FluentCMS.Database.Sqlite

SQLite provider implementation for FluentCMS database management with library-based multi-database support.

## Overview

This package provides a robust SQLite implementation of the FluentCMS database management interfaces. It supports both file-based and in-memory SQLite databases, offering excellent performance for development, testing, and production scenarios where lightweight database solutions are preferred.

## Key Features

- **File-based and In-memory SQLite** support
- **Automatic database creation** with directory structure setup
- **Robust table existence checking** using SQLite system tables
- **Comprehensive logging** for debugging and monitoring
- **Connection string parsing** with SqliteConnectionStringBuilder
- **SQL batch execution** for schema scripts
- **Library-based mapping** integration with FluentCMS.Database.Extensions

## Core Components

### SqliteDatabaseManager

Main implementation of `IDatabaseManager` for SQLite:

```csharp
internal sealed class SqliteDatabaseManager : IDatabaseManager
{
    public Task<bool> DatabaseExists(CancellationToken cancellationToken = default);
    public Task CreateDatabase(CancellationToken cancellationToken = default);
    public Task<bool> TablesExist(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);
    public Task<bool> TablesEmpty(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);
    public Task ExecuteRaw(string sql, CancellationToken cancellationToken = default);
}
```

### SqliteExtensions

Configuration extension methods for integrating with the FluentCMS configuration system:

```csharp
public static class SqliteExtensions
{
    public static ILibraryMappingBuilder UseSqlite(this ILibraryMappingBuilder builder, string connectionString);
}
```

## Configuration Examples

### Basic Configuration

```csharp
using FluentCMS.Database.Extensions;
using FluentCMS.Database.Sqlite;

services.AddDatabaseManager(options =>
{
    // Default SQLite database
    options.SetDefault().UseSqlite("Data Source=app.db;");
});
```

### File-based Database

```csharp
services.AddDatabaseManager(options =>
{
    // File database with specific path
    options.SetDefault().UseSqlite("Data Source=C:\\Data\\cms.db;");
    
    // Relative path database
    options.MapLibrary<IContentLibraryMarker>()
           .UseSqlite("Data Source=./databases/content.db;");
});
```

### In-Memory Database

```csharp
services.AddDatabaseManager(options =>
{
    // In-memory database (perfect for testing)
    options.SetDefault().UseSqlite("Data Source=:memory:;");
    
    // Alternative in-memory syntax
    options.MapLibrary<ITestLibraryMarker>()
           .UseSqlite("Data Source=test.db;Mode=Memory;Cache=Shared;");
});
```

### Multi-Database Setup

```csharp
services.AddDatabaseManager(options =>
{
    // Main application database
    options.SetDefault()
           .UseSqlite("Data Source=main.db;");
    
    // Content management database
    options.MapLibrary<IContentLibraryMarker>()
           .UseSqlite("Data Source=content.db;");
    
    // User data database
    options.MapLibrary<IUserLibraryMarker>()
           .UseSqlite("Data Source=users.db;");
    
    // Analytics database (in-memory for fast access)
    options.MapLibrary<IAnalyticsLibraryMarker>()
           .UseSqlite("Data Source=:memory:;");
});
```

## Usage Examples

### Service Implementation

```csharp
public class ContentService
{
    private readonly IDatabaseManager<IContentLibraryMarker> _db;
    private readonly ILogger<ContentService> _logger;
    
    public ContentService(
        IDatabaseManager<IContentLibraryMarker> databaseManager,
        ILogger<ContentService> logger)
    {
        _db = databaseManager;
        _logger = logger;
    }
    
    public async Task InitializeDatabase(CancellationToken cancellationToken = default)
    {
        // Create database if it doesn't exist
        if (!await _db.DatabaseExists(cancellationToken))
        {
            await _db.CreateDatabase(cancellationToken);
            _logger.LogInformation("Content database created successfully");
        }
        
        // Check and create tables
        var requiredTables = new[] { "Contents", "Categories", "Tags" };
        if (!await _db.TablesExist(requiredTables, cancellationToken))
        {
            await CreateSchema(cancellationToken);
        }
    }
    
    private async Task CreateSchema(CancellationToken cancellationToken)
    {
        var schema = @"
            CREATE TABLE Contents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Body TEXT,
                CategoryId INTEGER,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(CategoryId) REFERENCES Categories(Id)
            );
            
            CREATE TABLE Categories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT
            );
            
            CREATE TABLE Tags (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE
            );
            
            CREATE TABLE ContentTags (
                ContentId INTEGER,
                TagId INTEGER,
                PRIMARY KEY(ContentId, TagId),
                FOREIGN KEY(ContentId) REFERENCES Contents(Id),
                FOREIGN KEY(TagId) REFERENCES Tags(Id)
            );";
        
        await _db.ExecuteRaw(schema, cancellationToken);
        _logger.LogInformation("Content database schema created successfully");
    }
}
```

### Database Operations

```csharp
public class DatabaseService
{
    private readonly IDatabaseManager<IDefaultLibraryMarker> _db;
    
    public DatabaseService(IDatabaseManager<IDefaultLibraryMarker> databaseManager)
    {
        _db = databaseManager;
    }
    
    public async Task<bool> IsSchemaReady(CancellationToken cancellationToken = default)
    {
        var tables = new[] { "Users", "Roles", "Permissions" };
        return await _db.TablesExist(tables, cancellationToken);
    }
    
    public async Task<bool> IsDataEmpty(CancellationToken cancellationToken = default)
    {
        var tables = new[] { "Users", "Roles" };
        return await _db.TablesEmpty(tables, cancellationToken);
    }
    
    public async Task ExecuteMigration(string migrationScript, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteRaw(migrationScript, cancellationToken);
    }
}
```

## Connection String Options

SQLite supports various connection string parameters:

```csharp
// Basic file database
"Data Source=database.db;"

// In-memory database
"Data Source=:memory:;"

// Read-only database
"Data Source=readonly.db;Mode=ReadOnly;"

// Shared cache in-memory
"Data Source=shared;Mode=Memory;Cache=Shared;"

// Custom timeout and pooling
"Data Source=app.db;Connection Timeout=30;Pooling=true;"

// Full path with journal mode
"Data Source=C:\\Data\\app.db;Journal Mode=WAL;"
```

## Features and Capabilities

### Database Detection

- **File databases**: Checks for file existence on disk
- **In-memory databases**: Attempts connection to verify accessibility
- **Automatic directory creation**: Creates parent directories for file databases

### Table Management

- **Table existence checking**: Uses SQLite's `sqlite_master` system table
- **Case-insensitive table names**: Handles table name variations gracefully
- **Bulk table validation**: Efficiently checks multiple tables in single operation
- **Empty table detection**: Optimized queries using `LIMIT 1` for performance

### SQL Execution

- **Raw SQL support**: Execute any valid SQLite SQL commands
- **Transaction support**: Inherits SQLite's transaction capabilities
- **Schema migration support**: Execute complex DDL scripts
- **Parametrized queries**: Built-in protection against SQL injection

## Advanced Configuration

### Development Environment

```csharp
services.AddDatabaseManager(options =>
{
    if (Environment.IsDevelopment())
    {
        // Use in-memory for rapid development
        options.SetDefault().UseSqlite("Data Source=:memory:;");
    }
    else
    {
        // Use persistent file database
        options.SetDefault().UseSqlite("Data Source=production.db;Journal Mode=WAL;");
    }
});
```

### Testing Setup

```csharp
// In test configuration
services.AddDatabaseManager(options =>
{
    // Each test gets its own in-memory database
    options.SetDefault().UseSqlite("Data Source=:memory:;");
});

// Or use shared in-memory for test suites
services.AddDatabaseManager(options =>
{
    options.SetDefault().UseSqlite("Data Source=test;Mode=Memory;Cache=Shared;");
});
```

## Performance Considerations

1. **WAL Mode**: Use `Journal Mode=WAL;` for better concurrent access
2. **Connection Pooling**: Enable with `Pooling=true;` for multi-threaded scenarios
3. **In-Memory**: Use for caching or temporary data processing
4. **File Location**: Place database files on fast storage (SSD) for optimal performance

## Logging

The SQLite provider includes comprehensive logging:

- Database creation and existence checks
- Table validation operations
- SQL execution details
- Connection lifecycle events
- Error conditions with context

Configure logging levels to control verbosity:

```csharp
// In appsettings.json
{
  "Logging": {
    "LogLevel": {
      "FluentCMS.Database.Sqlite": "Information"
    }
  }
}
```

## Dependencies

This package depends on:
- **FluentCMS.Database.Abstractions**: Core interfaces
- **FluentCMS.Database.Extensions**: Configuration extensions
- **Microsoft.Data.Sqlite**: SQLite data provider
- **Microsoft.Extensions.Logging.Abstractions**: Logging infrastructure

## Target Framework

- **.NET 9.0**

## Related Packages

- **FluentCMS.Database.Abstractions**: Core interfaces and abstractions
- **FluentCMS.Database.Extensions**: Configuration and dependency injection
- **FluentCMS.Database.SqlServer**: SQL Server provider implementation
