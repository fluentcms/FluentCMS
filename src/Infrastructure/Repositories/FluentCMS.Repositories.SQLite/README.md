# FluentCMS.Repositories.SQLite

A .NET library that provides SQLite database provider configuration for FluentCMS Entity Framework repositories. This package enables easy configuration of SQLite databases with optional provider-specific settings within the FluentCMS repository framework.

## Overview

This library extends the `FluentCMS.Repositories.EntityFramework.Configuration` library to support SQLite as a database provider. It provides a fluent API for configuring SQLite connections and options within the multi-database configuration system supported by FluentCMS.

## Features

- **SQLite Integration**: Seamless integration with Microsoft Entity Framework Core SQLite provider
- **Fluent Configuration**: Uses the same fluent API as other database providers in the FluentCMS ecosystem
- **Provider-Specific Options**: Support for SQLite-specific configuration options
- **Multi-Database Support**: Works alongside other database providers in the same application
- **Connection String Management**: Simplified SQLite connection string configuration

## Installation

To install via NuGet:

```bash
dotnet add package FluentCMS.Repositories.SQLite
```

### Dependencies

This package depends on:
- FluentCMS.Repositories.EntityFramework.Configuration
- Microsoft.EntityFrameworkCore.Sqlite (>= 9.0.9)

## Usage

### Basic SQLite Configuration

First, configure your database manager in `Program.cs` or `Startup.cs` with SQLite support:

```csharp
using FluentCMS.Repositories.EntityFramework.Configuration;
using FluentCMS.Repositories.Sqlite;

// Configure SQLite database
builder.Services.AddDatabaseManager(options =>
{
    options.Default()
        .UseSqlite("Data Source=myapp.db");
});
```

Then register your DbContext:

```csharp
services.AddDatabaseContext<MyDbContext>();
```

### Multiple Databases

You can use SQLite alongside other database providers:

```csharp
using FluentCMS.Repositories.EntityFramework.Configuration;
using FluentCMS.Repositories.Sqlite;
// Assuming you have SQL Server extensions too
// using FluentCMS.Repositories.SqlServer;

public interface ICacheDatabase : IDatabaseArea { }

builder.Services.AddDatabaseManager(options =>
{
    // Main database (SQL Server)
    options.Default()
        .UseSqlServer("Server=localhost;Database=main;Trusted_Connection=True;");

    // Cache database (SQLite for local caching)
    options.For<ICacheDatabase>()
        .UseSqlite("Data Source=cache.db");
});

// Register DbContexts
services.AddDatabaseContext<MainDbContext>();
services.AddDatabaseContext<CacheDbContext, ICacheDatabase>();
```

### SQLite-Specific Options

Configure SQLite with provider-specific options:

```csharp
builder.Services.AddDatabaseManager(options =>
{
    options.Default()
        .UseSqlite(
            "Data Source=myapp.db;Foreign Keys=True",
            sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(30);
                sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
});
```

### File-Based SQLite Databases

SQLite supports file-based databases, making it ideal for:
- Single-user applications
- Development and testing environments
- Embedded scenarios
- Local caching layers

```csharp
// File database
.UseSqlite("Data Source=local.db")

// In-memory database (note: not persisted between application restarts)
.UseSqlite("DataSource=:memory:")

// Relative path
.UseSqlite("Data Source=./data/myapp.db")

// Absolute path
.UseSqlite("Data Source=C:/databases/myapp.db")
```

## API Reference

### Extension Methods

#### UseSqlite (DatabaseConfigurationBuilder)

```csharp
public static DatabaseConfigurationBuilder UseSqlite(
    this DatabaseConfigurationBuilder builder,
    string connectionString,
    Action<SqliteDbContextOptionsBuilder>? sqliteOptionsAction = null)
```

Configures the database to use SQLite with the specified connection string and optional SQLite-specific configuration.

**Parameters:**
- `builder`: The database configuration builder
- `connectionString`: The SQLite connection string (e.g., `"Data Source=myapp.db"`)
- `sqliteOptionsAction`: Optional action to configure SQLite-specific options

**Returns:** The configuration builder for chaining

**Example:**
```csharp
.UseSqlite("Data Source=myapp.db")
```

## Configuration Examples

### Development Configuration

```csharp
builder.Services.AddDatabaseManager(options =>
{
    options.Default()
        .UseSqlite("Data Source=dev.db")
        .EnableDataSeeding(seeding =>
        {
            seeding.AddCondition(new DevelopmentEnvironmentCondition());
        });
});
```

### Production Configuration with Encryption

```csharp
builder.Services.AddDatabaseManager(options =>
{
    options.Default()
        // SQLite supports encrypted databases with commercial extensions
        .UseSqlite("Data Source=production.db;Password=myPassword;")
        .EnableSchemaValidation(validation =>
        {
            validation.AddCondition(new AlwaysExecuteCondition());
        });
});
```

### Multi-Tenant Configuration

```csharp
public interface ITenantADatabase : IDatabaseArea { }
public interface ITenantBDatabase : IDatabaseArea { }

builder.Services.AddDatabaseManager(options =>
{
    // Shared database
    options.Default()
        .UseSqlite("Data Source=shared.db");

    // Tenant A database
    options.For<ITenantADatabase>()
        .UseSqlite("Data Source=tenantA.db");

    // Tenant B database
    options.For<ITenantBDatabase>()
        .UseSqlite("Data Source=tenantB.db");
});
```

## Best Practices

### Connection Strings

- **File Paths**: Use absolute paths for production environments
- **Security**: Avoid embedding credentials in connection strings (use configuration files)
- **Foreign Keys**: Enable foreign key constraints with `Foreign Keys=True`
- **Performance**: Consider using WAL mode for concurrent access: `PRAGMA journal_mode=WAL`

### Performance Tuning

```csharp
.UseSqlite("Data Source=myapp.db", options =>
{
    // Adjust command timeout for long-running operations
    options.CommandTimeout(60);

    // Enable query splitting for complex queries
    options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
})
```

### Testing

SQLite is excellent for integration testing due to its:
- Fast startup times
- File-based nature (easy cleanup)
- ACID compliance
- Same SQL dialect as production databases

Example test setup:

```csharp
public class TestFixture : IDisposable
{
    public TestFixture()
    {
        // Create unique test database
        var connectionString = $"Data Source=test_{Guid.NewGuid()}.db";

        Services = new ServiceCollection();
        Services.AddDatabaseManager(options =>
        {
            options.Default().UseSqlite(connectionString);
        });
        Services.AddDatabaseContext<TestDbContext>();

        // Build and run migrations/seeders
    }

    public void Dispose()
    {
        // Cleanup test database file
    }
}
```

## Architecture Integration

This library integrates with the broader FluentCMS repository architecture:

- **DatabaseConfigurationBuilder**: Configures database provider and connection
- **ServiceCollectionExtensions**: Registers DbContexts with appropriate configurations
- **DatabaseManagerOptions**: Supports multiple database configurations with markers
- **Initialization System**: Works with data seeding and schema validation

The SQLite configuration follows the same pattern as other database providers:
1. Configure database provider via extension method
2. Register DbContexts with marker interfaces (optional)
3. Automatic initialization (schema validation/data seeding) on startup

## Limitations

- **Concurrency**: SQLite has locking limitations for high-concurrency write scenarios
- **Scalability**: File-based nature limits horizontal scaling compared to server databases
- **Network Access**: No native client-server architecture (consider alternatives for distributed applications)

## See Also

- [FluentCMS.Repositories.EntityFramework.Configuration](../../FluentCMS.Repositories.EntityFramework.Configuration/README.md) - Base configuration library
- [FluentCMS.Repositories.SqlServer](../../FluentCMS.Repositories.SqlServer/README.md) - SQL Server provider
- [Microsoft.EntityFrameworkCore.Sqlite](https://docs.microsoft.com/en-us/ef/core/providers/sqlite/) - Official SQLite provider documentation

## Contributing

When contributing:
- Follow the coding conventions of the FluentCMS project
- Write inline comments instead of XML documentation
- Always pass CancellationToken with default value to all async methods
- Do not add "Async" suffix to async method names
- Keep README.md updated with new capabilities

## License

This project is part of FluentCMS and follows the same licensing terms.
