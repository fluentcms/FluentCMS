# FluentCMS.Repositories.SqlServer

A .NET library providing SQL Server database configuration for the FluentCMS repository framework. This library extends the Entity Framework Configuration to enable SQL Server as a database provider.

## Overview

FluentCMS.Repositories.SqlServer is a lightweight extension library that provides configuration methods for using SQL Server with the FluentCMS Entity Framework repository system. It provides a fluent API for configuring SQL Server connections and specific options.

## Features

- SQL Server database provider integration
- Fluextensible configuration options via `SqlServerDbContextOptionsBuilder`
- Full integration with FluentCMS database management system
- Support for custom connection strings
- Optional SQL Server-specific configuration

## Installation

### Package Reference

Add the package reference to your project file:

```xml
<PackageReference Include="FluentCMS.Repositories.SqlServer" Version="1.0.0" />
```

Or using the dotnet CLI:

```bash
dotnet add package FluentCMS.Repositories.SqlServer
```

### Dependencies

This library has the following dependencies:

- `Microsoft.EntityFrameworkCore.SqlServer` (>= 9.0.9)
- `FluentCMS.Repositories.EntityFramework.Configuration` (project reference)

## Usage

### Basic Configuration

To configure SQL Server as your database provider, use the `UseSqlServer` extension method during database manager configuration:

```csharp
using FluentCMS.Repositories.EntityFramework.Configuration;
using FluentCMS.Repositories.SqlServer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDatabaseManager(options =>
{
    options.Default()
        .UseSqlServer("Server=your_server;Database=your_database;Trusted_Connection=True;");
});
```

### Advanced Configuration

For more advanced SQL Server configuration, you can provide additional options via the `sqlServerOptionsAction` parameter:

```csharp
builder.Services.AddDatabaseManager(options =>
{
    options.Default()
        .UseSqlServer(
            connectionString: "Server=your_server;Database=your_database;User Id=your_user;Password=your_password;",
            sqlServerOptionsAction: sqlOptions =>
            {
                // Configure SQL Server-specific options
                sqlOptions.CommandTimeout(60);
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "custom_schema");
                sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
});
```

### Multiple Database Configuration

The library supports configuring multiple databases with different markers:

```csharp
builder.Services.AddDatabaseManager(options =>
{
    // Default database
    options.Default()
        .UseSqlServer("Server=primary_server;Database=primary_db;Trusted_Connection=True;");

    // Secondary database for specific entities
    options.For<SecondaryDbMarker>()
        .UseSqlServer("Server=secondary_server;Database=secondary_db;Trusted_Connection=True;");
});
```

## Architecture

### DatabaseConfigurationBuilder Extension

The `UseSqlServer` method extends the `DatabaseConfigurationBuilder` class, which is part of the FluentCMS.EntityFramework.Configuration package. This pattern allows for a fluent, extensible configuration API.

### Key Components

- **`SqlServerDatabaseConfiguration`**: Static class providing extension methods for configuration
- **`UseSqlServer`**: Extension method that configures Entity Framework to use SQL Server
- **Connection String Management**: Secure handling of database connection strings
- **Options Builder**: Integration with Entity Framework's `SqlServerDbContextOptionsBuilder`

## Configuration Options

### Connection String

The connection string should include all necessary SQL Server connection parameters:

- `Server`: SQL Server instance name or IP address
- `Database`: Database name
- `User Id` and `Password`: SQL Server authentication
- `Trusted_Connection`: Windows authentication (True/False)
- Additional parameters as needed (connection timeout, pooling, etc.)

### SQL Server Options

The `sqlServerOptionsAction` parameter accepts a lambda that configures:

- Command timeout settings
- Migration history table configuration
- Query splitting behavior
- Other SQL Server-specific Entity Framework options

## Integration with FluentCMS

### Database Manager Integration

This library integrates seamlessly with the FluentCMS database manager system:

```csharp
// Register DbContext with SQL Server configuration
builder.Services.AddDatabaseContext<ApplicationDbContext>(
    lifetime: ServiceLifetime.Scoped);
```

### Marker-Based Configuration

Use marker interfaces to associate specific DbContexts with SQL Server configurations:

```csharp
// Define a marker interface
public interface IReportingDatabase : IDatabaseArea { }

// Configure SQL Server for reporting
options.For<IReportingDatabase>()
    .UseSqlServer("Server=reporting_server;Database=reporting_db;...");

// Register DbContext with marker
builder.Services.AddDatabaseContext<ReportingDbContext, IReportingDatabase>();
```

## Best Practices

### Connection String Security

- Store connection strings securely (environment variables, Azure Key Vault, etc.)
- Use integrated authentication when possible in development
- Avoid hardcoding credentials in source code

### Performance Considerations

- Configure appropriate command timeouts for long-running operations
- Consider query splitting for complex queries
- Monitor connection pool usage in high-traffic scenarios

### Error Handling

- Implement proper exception handling around database operations
- Log connection issues for troubleshooting
- Consider retry policies for transient failures

## Examples

See the [integration tests](../FluentCMS.Repositories.Tests.Integration/) for comprehensive examples of SQL Server configuration and usage patterns.

## Contributing

Contributions are welcome! Please see the main FluentCMS repository for contribution guidelines.

## License

This project is licensed under the terms specified in the main FluentCMS repository.

## Dependencies

- .NET 9.0
- Microsoft.EntityFrameworkCore.SqlServer 9.0.9
- FluentCMS.Repositories.EntityFramework.Configuration
