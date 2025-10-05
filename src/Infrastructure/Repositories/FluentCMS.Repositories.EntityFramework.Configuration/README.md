# FluentCMS.Repositories.EntityFramework.Configuration

A .NET library that provides configuration management for Entity Framework Core database connections in FluentCMS applications. It supports multiple database configurations, schema validation, and data seeding with priority-based execution.

## Overview

This library simplifies the configuration and management of multiple Entity Framework DbContexts in a single application. It allows you to:

- Configure multiple databases with different providers and connection strings
- Associate DbContexts with specific databases using marker interfaces
- Automatically validate database schemas and seed initial data on application startup
- Execute seeding and validation operations in global priority order across all configured databases

## Features

- **Multi-Database Support**: Configure different databases for different DbContexts using marker interfaces
- **Automatic Initialization**: Database schema creation and data seeding runs automatically on application startup
- **Priority-Based Execution**: Data seeders and schema validators execute in priority order globally
- **Conditional Operations**: Skip seeding/validation based on custom conditions
- **Flexible Configuration**: Support for various database providers (SQL Server, SQLite, PostgreSQL, etc.)
- **Hosted Service Integration**: Runs initialization during application startup via ASP.NET Core's hosted services

## Installation

To install via NuGet:

```bash
dotnet add package FluentCMS.Repositories.EntityFramework.Configuration
```

This package depends on:
- FluentCMS.Repositories.Abstractions
- FluentCMS.Repositories.DataInitialization
- FluentCMS.Repositories.DataInitialization.EntityFramework
- Microsoft.EntityFrameworkCore
- Microsoft.Extensions.Hosting

## Usage

### Basic Configuration

First, configure your database manager in `Program.cs` or `Startup.cs`:

```csharp
using FluentCMS.Repositories.EntityFramework.Configuration;

// Example configuration for SQL Server
builder.Services.AddDatabaseManager(options =>
{
    options.Default()
        .ConfigureOptions(builder =>
        {
            builder.UseSqlServer("your-connection-string-here");
        });
});
```

Then register your DbContexts:

```csharp
// For default database
services.AddDatabaseContext<MyDbContext>();

// For specific marker-based database
services.AddDatabaseContext<MyDbContext, IMyDatabaseMarker>();
```

### Multiple Databases with Markers

You can configure multiple databases by defining marker interfaces that implement `IDatabaseArea`:

```csharp
public interface IOrdersDatabase : IDatabaseArea { }
public interface IProductsDatabase : IDatabaseArea { }

builder.Services.AddDatabaseManager(options =>
{
    // Default database (SQL Server for common data)
    options.Default()
        .ConfigureOptions(builder =>
        {
            builder.UseSqlServer("main-connection-string");
        });

    // Orders database (PostgreSQL)
    options.For<IOrdersDatabase>()
        .ConfigureOptions(builder =>
        {
            builder.UseNpgsql("orders-connection-string");
        });

    // Products database (SQLite for local data)
    options.For<IProductsDatabase>()
        .ConfigureOptions(builder =>
        {
            builder.UseSqlite("products-connection-string");
        });
});
```

Then register DbContexts with their markers:

```csharp
services.AddDatabaseContext<OrdersDbContext, IOrdersDatabase>();
services.AddDatabaseContext<ProductsDbContext, IProductsDatabase>();
services.AddDatabaseContext<UserDbContext>(); // Uses default
```

### Schema Validation

Enable automatic schema validation on startup:

```csharp
builder.Services.AddDatabaseManager(options =>
{
    options.Default()
        .ConfigureOptions(builder => builder.UseSqlServer("connection-string"))
        .EnableSchemaValidation(validation =>
        {
            validation.AddCondition(new MyValidationCondition());
            validation.IgnoreExceptions = false;
        });
});

// Register schema validators
services.AddSchemaValidator<MySchemaValidator>();
services.AddSchemaValidator<MySchemaValidator, IMyDatabaseMarker>(); // For specific database
```

Schema validators must implement `ISchemaValidator`:

```csharp
public class MySchemaValidator : ISchemaValidator
{
    public int Priority => 100;

    public async Task<bool> ValidateSchema(CancellationToken cancellationToken = default)
    {
        // Check if schema is valid
        return await SchemaExists(cancellationToken);
    }

    public async Task CreateSchema(CancellationToken cancellationToken = default)
    {
        // Create/update schema
        await RunMigrations(cancellationToken);
    }
}
```

### Data Seeding

Enable automatic data seeding:

```csharp
builder.Services.AddDatabaseManager(options =>
{
    options.Default()
        .ConfigureOptions(builder => builder.UseSqlServer("connection-string"))
        .EnableDataSeeding(seeding =>
        {
            seeding.AddCondition(new DataSeedingCondition());
            seeding.IgnoreExceptions = false;
        });
});

// Register data seeders
services.AddDataSeeder<MyDataSeeder>();
services.AddDataSeeder<MyDataSeeder, IMyDatabaseMarker>(); // For specific database
```

Data seeders must implement `IDataSeeder`:

```csharp
public class MyDataSeeder : IDataSeeder
{
    public int Priority => 200; // Lower number = higher priority

    public async Task<bool> ShouldSeed(CancellationToken cancellationToken = default)
    {
        // Check if data already exists
        return !await AnyDataExists(cancellationToken);
    }

    public async Task SeedData(CancellationToken cancellationToken = default)
    {
        // Insert initial data
        await InsertSeedData(cancellationToken);
    }
}
```

## Architecture

### Key Components

- **`DatabaseConfiguration`**: Contains database provider settings, connection string, seeding/validation options
- **`DatabaseManagerOptions`**: Manages multiple database configurations (default + marker-based)
- **`DatabaseConfigurationBuilder`**: Fluent builder for configuring database settings
- **`ServiceCollectionExtensions`**: Extension methods for configuring DI container

### Initialization Flow

1. Application starts
2. `DataInitializerHostedService` begins
3. Schema validators execute for all databases in priority order
4. Data seeders execute for all databases in priority order
5. Application continues with properly initialized databases

### Execution Order

- Validators and seeders across all databases execute globally in priority order
- Priority determines execution sequence (lower number = higher priority)
- Conditions are checked before execution
- Exceptions can be ignored based on configuration

## Advanced Usage

### Custom Conditions

Implement `IExecutionCondition` for custom validation/seeding logic:

```csharp
public class EnvironmentCondition : IExecutionCondition
{
    public string Name => "Environment Check";

    public async Task<bool> ShouldExecute(CancellationToken cancellationToken = default)
    {
        // Only run in development environment
        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
    }
}
```

### Service Registration Patterns

```csharp
// Register shared seeders/validators for all databases
services.AddDataSeeder<CommonDataSeeder>();
services.AddSchemaValidator<CommonSchemaValidator>();

// Register specific ones
services.AddDataSeeder<UserSeeder, IOrdersDatabase>();
services.AddSchemaValidator<ProductSchemaValidator, IProductsDatabase>();
```

### Dependency Injection

The library integrates fully with .NET's DI container. All seeders and validators are registered as keyed services and resolved from `IServiceProvider`.

## API Reference

### Core Classes

#### DatabaseConfiguration
- `ConfigureOptions`: Delegate to configure DbContextOptionsBuilder
- `ConnectionString`: Database connection string
- `SeedingOptions`: Optional data seeding configuration
- `SchemaValidationOptions`: Optional schema validation configuration

#### DatabaseManagerOptions
- `Default()`: Configure default database
- `For<TMarker>()`: Configure database for marker type
- `GetDefaultConfiguration()`: Retrieve default config
- `GetConfigurationForMarker(Type)`: Get config by marker type

#### ServiceCollectionExtensions
- `AddDatabaseManager(Action)`: Configure database manager
- `AddDatabaseContext<TContext>()`: Register DbContext with default db
- `AddDatabaseContext<TContext, TMarker>()`: Register DbContext with marker db
- `EnableDataSeeding(Action)`: Enable and configure seeding
- `EnableSchemaValidation(Action)`: Enable and configure validation
- `AddDataSeeder<T>()`: Register seeder for default db
- `AddDataSeeder<TSeeder, TMarker>()`: Register seeder for marker db
- `AddSchemaValidator<T>()`: Register validator for default db
- `AddSchemaValidator<T, TMarker>()`: Register validator for marker db

### Interfaces
- `IDatabaseArea`: Marker interface for database areas
- `IDataSeeder`: Data seeding interface
- `ISchemaValidator`: Schema validation interface
- `IExecutionCondition`: Condition checking interface

### Exceptions
- `InvalidOperationException`: Thrown when DatabaseManager is not configured before registering DbContexts

## Examples

### Complete Configuration Example

```csharp
public interface IAuditDatabase : IDatabaseArea { }
public interface IContentDatabase : IDatabaseArea { }

var builder = WebApplication.CreateBuilder(args);

// Configure databases
builder.Services.AddDatabaseManager(options =>
{
    // Default SQL Server database
    options.Default()
        .ConfigureOptions(b =>
        {
            b.UseSqlServer("Server=localhost;Database=main;Trusted_Connection=True;");
        })
        .EnableSchemaValidation(v =>
        {
            v.AddCondition(new AlwaysExecuteCondition());
        })
        .EnableDataSeeding(s =>
        {
            s.AddCondition(new DataNotExistsCondition());
        });

    // Audit database (separate for compliance)
    options.For<IAuditDatabase>()
        .ConfigureOptions(b =>
        {
            b.UseSqlServer("Server=audit-server;Database=audit;Trusted_Connection=True;");
        })
        .EnableSchemaValidation(v =>
        {
            v.AddCondition(new AlwaysExecuteCondition());
        });
});

// Register DbContexts
builder.Services.AddDatabaseContext<UserDbContext>();
builder.Services.AddDatabaseContext<AuditDbContext, IAuditDatabase>();
builder.Services.AddDatabaseContext<ContentDbContext, IContentDatabase>(); // Falls back to default

// Register seeders
builder.Services.AddDataSeeder<UserSeeder>();
builder.Services.AddDataSeeder<AuditSeeder, IAuditDatabase>();
builder.Services.AddSchemaValidator<BaseSchemaValidator>();

var app = builder.Build();
app.Run();
```

This setup ensures:
- User and content data goes to the main database
- Audit data goes to the separate audit database
- All databases get schema validation
- Only the main database gets data seeding
- Operations execute with proper priority ordering

## Contributing

When contributing:
- Follow the coding conventions
- Write inline comments instead of XML documentation
- Pass `CancellationToken` with default value to all async methods
- Do not add "Async" suffix to async method names
- Keep README.md and CHANGELOG.md updated with new capabilities

## License

This project is part of FluentCMS and follows the same licensing terms.
