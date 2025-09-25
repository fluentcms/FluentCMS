# FluentCMS Database Management System

A comprehensive, library-based multi-database management system for .NET applications with type-safe configuration and high-performance dependency injection.

## Overview

FluentCMS Database provides a powerful abstraction layer for managing multiple databases in complex applications. It uses a marker-based pattern to enable different parts of your application to use different database providers while maintaining compile-time type safety and zero-runtime overhead.

## 🚀 Key Features

- **Library-Based Multi-Database Support**: Different modules can use different databases
- **Type-Safe Configuration**: Compile-time validation with generic constraints
- **High-Performance Resolution**: Direct dependency injection without reflection
- **Multiple Database Providers**: SQL Server, SQLite, and extensible to others
- **Async-First Design**: Full async/await support throughout
- **Comprehensive Logging**: Detailed logging for debugging and monitoring
- **Enterprise-Ready**: Production-tested with robust error handling

## 📦 Package Structure

| Package | Purpose | Dependencies |
|---------|---------|--------------|
| **FluentCMS.Database.Abstractions** | Core interfaces and contracts | Microsoft.Extensions.DI/Logging.Abstractions |
| **FluentCMS.Database.Extensions** | Configuration and DI integration | Abstractions |
| **FluentCMS.Database.SqlServer** | SQL Server provider | Extensions, Microsoft.Data.SqlClient |
| **FluentCMS.Database.Sqlite** | SQLite provider | Extensions, Microsoft.Data.Sqlite |

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Your Application                     │
├─────────────────────────────────────────────────────────┤
│  Service A          Service B          Service C        │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐    │
│  │IDatabaseMgr │   │IDatabaseMgr │   │IDatabaseMgr │    │
│  │<MarkerA>    │   │<MarkerB>    │   │<Default>    │    │
│  └─────────────┘   └─────────────┘   └─────────────┘    │
├─────────────────────────────────────────────────────────┤
│            FluentCMS.Database.Extensions                │
│              (Configuration & DI Layer)                 │
├─────────────────────────────────────────────────────────┤
│   SqlServer Provider    │    SQLite Provider            │
│   ┌─────────────────┐   │   ┌─────────────────┐         │
│   │ SqlServerDB     │   │   │ SqliteDB        │         │
│   │ Manager         │   │   │ Manager         │         │
│   └─────────────────┘   │   └─────────────────┘         │
├─────────────────────────┼───────────────────────────────┤
│          FluentCMS.Database.Abstractions                │
│              (Core Interfaces)                          │
└─────────────────────────────────────────────────────────┘
```

## 🚀 Quick Start

### 1. Install Packages

```bash
# Install core packages
dotnet add package FluentCMS.Database.Extensions

# Install database providers
dotnet add package FluentCMS.Database.SqlServer
dotnet add package FluentCMS.Database.Sqlite
```

### 2. Define Library Markers

```csharp
using FluentCMS.Database.Abstractions;

// Define markers for different modules
public interface IContentLibraryMarker : IDatabaseManagerMarker { }
public interface IUserLibraryMarker : IDatabaseManagerMarker { }
public interface IAnalyticsLibraryMarker : IDatabaseManagerMarker { }
```

### 3. Configure Database Providers

```csharp
using FluentCMS.Database.Extensions;
using FluentCMS.Database.SqlServer;
using FluentCMS.Database.Sqlite;

// In Program.cs or Startup.cs
services.AddDatabaseManager(options =>
{
    // Default database for general services
    options.SetDefault()
           .UseSqlServer("Server=.;Database=MainCMS;Integrated Security=true;");
    
    // Content management on dedicated SQL Server
    options.MapLibrary<IContentLibraryMarker>()
           .UseSqlServer("Server=content-db;Database=ContentCMS;Integrated Security=true;");
    
    // User data on separate database
    options.MapLibrary<IUserLibraryMarker>()
           .UseSqlServer("Server=user-db;Database=UserCMS;Integrated Security=true;");
    
    // Analytics using SQLite for performance
    options.MapLibrary<IAnalyticsLibraryMarker>()
           .UseSqlite("Data Source=analytics.db;");
});
```

### 4. Use in Services

```csharp
public class ContentService
{
    private readonly IDatabaseManager<IContentLibraryMarker> _db;
    
    public ContentService(IDatabaseManager<IContentLibraryMarker> databaseManager)
    {
        _db = databaseManager;
    }
    
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Create database if needed
        if (!await _db.DatabaseExists(cancellationToken))
        {
            await _db.CreateDatabase(cancellationToken);
        }
        
        // Check tables
        var tables = new[] { "Contents", "Categories" };
        if (!await _db.TablesExist(tables, cancellationToken))
        {
            await CreateSchema(cancellationToken);
        }
    }
    
    private async Task CreateSchema(CancellationToken cancellationToken)
    {
        var schema = @"
            CREATE TABLE Contents (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Title NVARCHAR(200) NOT NULL,
                Body NVARCHAR(MAX),
                CreatedAt DATETIME2 DEFAULT GETUTCDATE()
            );";
        
        await _db.ExecuteRaw(schema, cancellationToken);
    }
}
```

## 📋 Core Operations

### Database Management

```csharp
// Check if database exists
bool exists = await _db.DatabaseExists(cancellationToken);

// Create database if it doesn't exist
if (!exists)
{
    await _db.CreateDatabase(cancellationToken);
}
```

### Table Operations

```csharp
// Check if tables exist
var tables = new[] { "Users", "Roles", "Permissions" };
bool tablesExist = await _db.TablesExist(tables, cancellationToken);

// Check if tables are empty
bool isEmpty = await _db.TablesEmpty(tables, cancellationToken);
```

### SQL Execution

```csharp
// Execute raw SQL
var sql = @"
    CREATE TABLE Products (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        Price DECIMAL(10,2)
    );";

await _db.ExecuteRaw(sql, cancellationToken);
```

## 🏢 Enterprise Scenarios

### Multi-Tenant Architecture

```csharp
// Define tenant-specific markers
public interface ITenant1DatabaseMarker : IDatabaseManagerMarker { }
public interface ITenant2DatabaseMarker : IDatabaseManagerMarker { }

// Configure per-tenant databases
services.AddDatabaseManager(options =>
{
    options.MapLibrary<ITenant1DatabaseMarker>()
           .UseSqlServer("Server=tenant1-db;Database=Tenant1CMS;Integrated Security=true;");
    
    options.MapLibrary<ITenant2DatabaseMarker>()
           .UseSqlServer("Server=tenant2-db;Database=Tenant2CMS;Integrated Security=true;");
});

// Tenant-aware service
public class TenantService<TTenantMarker> where TTenantMarker : IDatabaseManagerMarker
{
    private readonly IDatabaseManager<TTenantMarker> _db;
    
    public TenantService(IDatabaseManager<TTenantMarker> databaseManager)
    {
        _db = databaseManager;
    }
}
```

### Microservices Architecture

```csharp
// Each microservice has its own database marker
public interface IOrderServiceMarker : IDatabaseManagerMarker { }
public interface IInventoryServiceMarker : IDatabaseManagerMarker { }
public interface IPaymentServiceMarker : IDatabaseManagerMarker { }

// Configure microservice databases
services.AddDatabaseManager(options =>
{
    options.MapLibrary<IOrderServiceMarker>()
           .UseSqlServer("Server=orders-db;Database=Orders;Integrated Security=true;");
    
    options.MapLibrary<IInventoryServiceMarker>()
           .UseSqlServer("Server=inventory-db;Database=Inventory;Integrated Security=true;");
    
    options.MapLibrary<IPaymentServiceMarker>()
           .UseSqlServer("Server=payments-db;Database=Payments;Integrated Security=true;");
});
```

### Environment-Specific Configuration

```csharp
services.AddDatabaseManager(options =>
{
    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    
    switch (environment)
    {
        case "Development":
            // Local SQLite for development
            options.SetDefault().UseSqlite("Data Source=dev.db;");
            break;
            
        case "Testing":
            // In-memory for testing
            options.SetDefault().UseSqlite("Data Source=:memory:;");
            break;
            
        case "Staging":
            // Staging SQL Server
            options.SetDefault().UseSqlServer(stagingConnectionString);
            break;
            
        case "Production":
            // Production with security
            options.SetDefault().UseSqlServer($"{prodConnectionString};Encrypt=true;");
            break;
    }
});
```

## 🧪 Testing Strategies

### Unit Testing

```csharp
[Test]
public async Task ShouldCreateContentSuccessfully()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDatabaseManager(options =>
    {
        options.SetDefault().UseSqlite("Data Source=:memory:;");
    });
    
    var provider = services.BuildServiceProvider();
    var contentService = new ContentService(
        provider.GetRequiredService<IDatabaseManager<IDefaultLibraryMarker>>());
    
    // Act
    await contentService.InitializeAsync();
    
    // Assert
    // Verify database operations...
}
```

### Integration Testing

```csharp
public class DatabaseIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    
    public DatabaseIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }
    
    [Fact]
    public async Task ShouldHandleMultipleDatabaseConnections()
    {
        // Test with real database connections
        using var scope = _factory.Services.CreateScope();
        
        var contentDb = scope.ServiceProvider
            .GetRequiredService<IDatabaseManager<IContentLibraryMarker>>();
        var userDb = scope.ServiceProvider
            .GetRequiredService<IDatabaseManager<IUserLibraryMarker>>();
        
        // Test operations across different databases
        Assert.True(await contentDb.DatabaseExists());
        Assert.True(await userDb.DatabaseExists());
    }
}
```

## ⚡ Performance Optimization

### Connection String Optimization

```csharp
// SQL Server performance settings
"Server=myserver;Database=MyDB;Integrated Security=true;" +
"MultipleActiveResultSets=true;" +          // Enable MARS
"Connection Timeout=60;" +                   // Connection timeout
"Command Timeout=300;" +                     // Command timeout
"Max Pool Size=200;" +                       // Connection pooling
"Application Name=FluentCMS;"                // Application identification

// SQLite performance settings
"Data Source=app.db;" +
"Journal Mode=WAL;" +                        // Write-Ahead Logging
"Cache Size=10000;" +                        // Cache size in pages
"Synchronous=NORMAL;" +                      // Synchronization mode
"Temp Store=MEMORY;"                         // Temporary storage
```

### Async Best Practices

```csharp
public class OptimizedService
{
    private readonly IDatabaseManager<IContentLibraryMarker> _db;
    
    public async Task BulkOperationAsync(CancellationToken cancellationToken = default)
    {
        // Use ConfigureAwait(false) for library code
        var exists = await _db.DatabaseExists(cancellationToken)
            .ConfigureAwait(false);
        
        if (!exists)
        {
            await _db.CreateDatabase(cancellationToken)
                .ConfigureAwait(false);
        }
        
        // Batch multiple operations
        var tasks = new[]
        {
            _db.TablesExist(new[] { "Table1" }, cancellationToken),
            _db.TablesExist(new[] { "Table2" }, cancellationToken),
            _db.TablesExist(new[] { "Table3" }, cancellationToken)
        };
        
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
```

## 🔧 Advanced Configuration

### Custom Database Provider

```csharp
// Implement custom database manager
public class CustomDatabaseManager : IDatabaseManager
{
    public Task<bool> DatabaseExists(CancellationToken cancellationToken = default)
    {
        // Custom implementation
        throw new NotImplementedException();
    }
    
    // Implement other methods...
}

// Create extension method
public static class CustomDatabaseExtensions
{
    public static ILibraryMappingBuilder UseCustomDatabase(
        this ILibraryMappingBuilder builder, 
        string connectionString)
    {
        return builder.RegisterProvider("CustomDB", connectionString, 
            (connString, serviceProvider) =>
            {
                var logger = serviceProvider.GetRequiredService<ILogger<CustomDatabaseManager>>();
                return new CustomDatabaseManager(connString, logger);
            });
    }
}

// Use in configuration
services.AddDatabaseManager(options =>
{
    options.MapLibrary<ICustomLibraryMarker>()
           .UseCustomDatabase("CustomConnectionString");
});
```

### Dynamic Configuration

```csharp
services.AddDatabaseManager(options =>
{
    var configuration = serviceProvider.GetService<IConfiguration>();
    var featureFlags = serviceProvider.GetService<IFeatureFlags>();
    
    // Dynamic default based on environment
    var defaultProvider = configuration["Database:DefaultProvider"];
    var defaultConnection = configuration.GetConnectionString("Default");
    
    if (defaultProvider == "SqlServer")
    {
        options.SetDefault().UseSqlServer(defaultConnection);
    }
    else
    {
        options.SetDefault().UseSqlite(defaultConnection);
    }
    
    // Conditional library mapping
    if (featureFlags.IsEnabled("AdvancedAnalytics"))
    {
        options.MapLibrary<IAnalyticsLibraryMarker>()
               .UseSqlServer(configuration.GetConnectionString("Analytics"));
    }
});
```

## 🐛 Troubleshooting

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| **InvalidOperationException: No default configuration** | Missing `SetDefault()` call | Add `options.SetDefault()` in configuration |
| **ArgumentException: Connection string cannot be null** | Empty connection string | Verify connection string values |
| **SqlException: Database does not exist** | Database not created | Ensure `CreateDatabase()` is called |
| **UnauthorizedAccessException** | File permissions | Check file/directory permissions for SQLite |
| **TimeoutException** | Network/performance issues | Increase connection timeout |

### Logging Configuration

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "FluentCMS.Database.SqlServer": "Debug",
      "FluentCMS.Database.Sqlite": "Debug",
      "FluentCMS.Database.Extensions": "Information"
    }
  }
}
```

### Debug Information

```csharp
// Enable detailed logging for troubleshooting
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.AddDebug();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// Log database operations
public class DiagnosticService
{
    private readonly IDatabaseManager<IContentLibraryMarker> _db;
    private readonly ILogger<DiagnosticService> _logger;
    
    public async Task RunDiagnosticsAsync()
    {
        _logger.LogInformation("Starting database diagnostics");
        
        try
        {
            var exists = await _db.DatabaseExists();
            _logger.LogInformation("Database exists: {Exists}", exists);
            
            var tables = new[] { "Contents", "Categories" };
            var tablesExist = await _db.TablesExist(tables);
            _logger.LogInformation("Tables exist: {TablesExist}", tablesExist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database diagnostic failed");
            throw;
        }
    }
}
```

## 📚 Additional Resources

### Documentation Links

- [FluentCMS.Database.Abstractions](FluentCMS.Database.Abstractions/README.md) - Core interfaces and contracts
- [FluentCMS.Database.Extensions](FluentCMS.Database.Extensions/README.md) - Configuration and DI integration  
- [FluentCMS.Database.SqlServer](FluentCMS.Database.SqlServer/README.md) - SQL Server provider
- [FluentCMS.Database.Sqlite](FluentCMS.Database.Sqlite/README.md) - SQLite provider

### Best Practices

1. **Always use CancellationToken**: Pass cancellation tokens to all async operations
2. **Create databases early**: Call `CreateDatabase()` during application startup
3. **Use appropriate timeouts**: Configure connection and command timeouts for your scenario
4. **Monitor performance**: Use logging to track database operation performance
5. **Handle exceptions**: Implement proper error handling for database operations
6. **Test with real databases**: Include integration tests with actual database providers

### Migration from Legacy Systems

```csharp
// Migrate from traditional DbContext pattern
public class LegacyMigrationService
{
    private readonly IDatabaseManager<ILegacyLibraryMarker> _db;
    
    public async Task MigrateFromLegacyAsync()
    {
        // Check if migration is needed
        var legacyTables = new[] { "LegacyUsers", "LegacyContent" };
        if (await _db.TablesExist(legacyTables))
        {
            // Perform migration
            await _db.ExecuteRaw(migrationScript);
        }
    }
}
```

## 🤝 Contributing

This is an enterprise-grade database management system designed for production use. Each component is thoroughly tested and optimized for performance and reliability.

## 📄 License

See the project license for usage terms and conditions.

---

**FluentCMS Database Management System** - Empowering .NET applications with type-safe, high-performance multi-database support.
