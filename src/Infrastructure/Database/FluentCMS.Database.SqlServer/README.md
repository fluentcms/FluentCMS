# FluentCMS.Database.SqlServer

SQL Server provider implementation for FluentCMS database management with library-based multi-database support.

## Overview

This package provides a comprehensive SQL Server implementation of the FluentCMS database management interfaces. It supports both traditional SQL Server databases and file-attached databases, offering enterprise-grade performance and features for production applications requiring robust database solutions.

## Key Features

- **Full SQL Server support** including Express, Standard, and Enterprise editions
- **File-attached database support** with automatic file creation
- **Master database operations** for database creation and management
- **Advanced connection handling** with SqlConnectionStringBuilder
- **Schema and table validation** using SQL Server system views
- **SQL batch processing** with GO statement support
- **Comprehensive error handling** and logging
- **Library-based mapping** integration with FluentCMS.Database.Extensions

## Core Components

### SqlServerDatabaseManager

Main implementation of `IDatabaseManager` for SQL Server:

```csharp
internal sealed class SqlServerDatabaseManager : IDatabaseManager
{
    public Task<bool> DatabaseExists(CancellationToken cancellationToken = default);
    public Task CreateDatabase(CancellationToken cancellationToken = default);
    public Task<bool> TablesExist(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);
    public Task<bool> TablesEmpty(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);
    public Task ExecuteRaw(string sql, CancellationToken cancellationToken = default);
}
```

### SqlServerExtensions

Configuration extension methods for integrating with the FluentCMS configuration system:

```csharp
public static class SqlServerExtensions
{
    public static ILibraryMappingBuilder UseSqlServer(this ILibraryMappingBuilder builder, string connectionString);
}
```

## Configuration Examples

### Basic Configuration

```csharp
using FluentCMS.Database.Extensions;
using FluentCMS.Database.SqlServer;

services.AddDatabaseManager(options =>
{
    // Default SQL Server database
    options.SetDefault().UseSqlServer("Server=.;Database=FluentCMS;Integrated Security=true;");
});
```

### Different Authentication Methods

```csharp
services.AddDatabaseManager(options =>
{
    // Windows Authentication
    options.SetDefault()
           .UseSqlServer("Server=.;Database=FluentCMS;Integrated Security=true;");
    
    // SQL Server Authentication
    options.MapLibrary<IContentLibraryMarker>()
           .UseSqlServer("Server=content-server;Database=ContentCMS;User Id=cms_user;Password=secure123;");
    
    // Azure SQL Database
    options.MapLibrary<ICloudLibraryMarker>()
           .UseSqlServer("Server=tcp:myserver.database.windows.net,1433;Database=CloudCMS;User ID=admin@myserver;Password=password;Encrypt=true;");
});
```

### File-Attached Databases

```csharp
services.AddDatabaseManager(options =>
{
    // Local file database with automatic creation
    options.SetDefault()
           .UseSqlServer("Server=.;Database=LocalCMS;AttachDbFilename=C:\\Data\\LocalCMS.mdf;Integrated Security=true;");
    
    // Development file database
    options.MapLibrary<IDevLibraryMarker>()
           .UseSqlServer("Server=.\\SQLEXPRESS;Database=DevCMS;AttachDbFilename=.\\App_Data\\DevCMS.mdf;Integrated Security=true;User Instance=true;");
});
```

### Multi-Database Enterprise Setup

```csharp
services.AddDatabaseManager(options =>
{
    // Main application database
    options.SetDefault()
           .UseSqlServer("Server=main-db;Database=FluentCMS;Integrated Security=true;");
    
    // Content management on dedicated server
    options.MapLibrary<IContentLibraryMarker>()
           .UseSqlServer("Server=content-db;Database=ContentCMS;User Id=content_user;Password=content_pass;");
    
    // User management with Always Encrypted
    options.MapLibrary<IUserLibraryMarker>()
           .UseSqlServer("Server=secure-db;Database=UserCMS;Integrated Security=true;Column Encryption Setting=Enabled;");
    
    // Analytics with read-only replica
    options.MapLibrary<IAnalyticsLibraryMarker>()
           .UseSqlServer("Server=analytics-replica;Database=AnalyticsCMS;ApplicationIntent=ReadOnly;Integrated Security=true;");
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
        var requiredTables = new[] { "dbo.Contents", "dbo.Categories", "dbo.Tags" };
        if (!await _db.TablesExist(requiredTables, cancellationToken))
        {
            await CreateSchema(cancellationToken);
        }
    }
    
    private async Task CreateSchema(CancellationToken cancellationToken)
    {
        var schema = @"
            CREATE TABLE [dbo].[Categories] (
                [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Name] nvarchar(100) NOT NULL UNIQUE,
                [Description] nvarchar(500) NULL,
                [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE()
            );
            GO
            
            CREATE TABLE [dbo].[Contents] (
                [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Title] nvarchar(200) NOT NULL,
                [Body] nvarchar(max) NULL,
                [CategoryId] int NULL,
                [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
                [UpdatedAt] datetime2 NULL,
                CONSTRAINT [FK_Contents_Categories] FOREIGN KEY ([CategoryId]) 
                    REFERENCES [dbo].[Categories] ([Id])
            );
            GO
            
            CREATE TABLE [dbo].[Tags] (
                [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Name] nvarchar(50) NOT NULL UNIQUE,
                [Color] nvarchar(7) NULL
            );
            GO
            
            CREATE TABLE [dbo].[ContentTags] (
                [ContentId] int NOT NULL,
                [TagId] int NOT NULL,
                PRIMARY KEY ([ContentId], [TagId]),
                CONSTRAINT [FK_ContentTags_Contents] FOREIGN KEY ([ContentId]) 
                    REFERENCES [dbo].[Contents] ([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_ContentTags_Tags] FOREIGN KEY ([TagId]) 
                    REFERENCES [dbo].[Tags] ([Id]) ON DELETE CASCADE
            );
            GO
            
            CREATE INDEX [IX_Contents_CategoryId] ON [dbo].[Contents] ([CategoryId]);
            CREATE INDEX [IX_Contents_CreatedAt] ON [dbo].[Contents] ([CreatedAt]);";
        
        await _db.ExecuteRaw(schema, cancellationToken);
        _logger.LogInformation("Content database schema created successfully");
    }
}
```

### Advanced Database Operations

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
        // Check for tables with schema specification
        var tables = new[] { "dbo.Users", "dbo.Roles", "auth.Permissions" };
        return await _db.TablesExist(tables, cancellationToken);
    }
    
    public async Task<bool> IsDataEmpty(CancellationToken cancellationToken = default)
    {
        var tables = new[] { "Users", "Roles" }; // Will default to dbo schema
        return await _db.TablesEmpty(tables, cancellationToken);
    }
    
    public async Task ExecuteMigration(string migrationScript, CancellationToken cancellationToken = default)
    {
        // Supports GO batching for complex migrations
        await _db.ExecuteRaw(migrationScript, cancellationToken);
    }
    
    public async Task RunMaintenanceScript(CancellationToken cancellationToken = default)
    {
        var maintenanceScript = @"
            -- Update statistics
            UPDATE STATISTICS [dbo].[Contents];
            GO
            
            -- Rebuild fragmented indexes
            ALTER INDEX ALL ON [dbo].[Contents] REBUILD;
            GO 5  -- Run 5 times
            
            -- Clean up old data
            DELETE FROM [dbo].[AuditLog] 
            WHERE [CreatedAt] < DATEADD(MONTH, -6, GETUTCDATE());
            GO";
        
        await _db.ExecuteRaw(maintenanceScript, cancellationToken);
    }
}
```

## Connection String Options

SQL Server supports extensive connection string parameters:

```csharp
// Basic local connection
"Server=.;Database=MyDB;Integrated Security=true;"

// Named instance
"Server=.\\SQLEXPRESS;Database=MyDB;Integrated Security=true;"

// Remote server with SQL authentication
"Server=myserver;Database=MyDB;User Id=myuser;Password=mypass;"

// Azure SQL Database
"Server=tcp:myserver.database.windows.net,1433;Database=MyDB;User ID=user@myserver;Password=pass;Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;"

// File-attached database
"Server=.;Database=MyDB;AttachDbFilename=C:\\Data\\MyDB.mdf;Integrated Security=true;"

// Advanced options
"Server=myserver;Database=MyDB;Integrated Security=true;MultipleActiveResultSets=true;Connection Timeout=60;Command Timeout=300;Application Name=FluentCMS;"

// Always Encrypted
"Server=myserver;Database=MyDB;Integrated Security=true;Column Encryption Setting=Enabled;"

// Read-only intent
"Server=myserver;Database=MyDB;Integrated Security=true;ApplicationIntent=ReadOnly;"
```

## Features and Capabilities

### Database Management

- **Database existence checking**: Uses `DB_ID()` function for reliable detection
- **Automatic database creation**: Creates databases with proper file placement
- **File-attached database support**: Handles `.mdf` and `.ldf` file creation
- **Master database operations**: Manages database lifecycle operations
- **Database state monitoring**: Waits for database to become online after creation

### Schema Operations

- **Table existence validation**: Uses `sys.tables` and `sys.schemas` system views
- **Multi-schema support**: Handles `schema.table` notation (defaults to `dbo`)
- **Case-insensitive operations**: Normalizes table names for consistent behavior
- **Bulk validation**: Efficiently checks multiple tables in single operation

### SQL Execution

- **GO statement processing**: Properly handles SQL Server batch separators
- **Batch repetition support**: Supports `GO n` syntax for repeated execution
- **Transaction support**: Inherits SQL Server's full transaction capabilities
- **Advanced SQL features**: Supports stored procedures, functions, and complex queries

## Advanced Configuration

### Environment-Specific Setup

```csharp
services.AddDatabaseManager(options =>
{
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    
    if (Environment.IsDevelopment())
    {
        // Local development with file database
        options.SetDefault().UseSqlServer(
            "Server=.\\SQLEXPRESS;Database=DevCMS;AttachDbFilename=.\\App_Data\\DevCMS.mdf;Integrated Security=true;");
    }
    else if (Environment.IsStaging())
    {
        // Staging environment
        options.SetDefault().UseSqlServer(connectionString);
    }
    else
    {
        // Production with enhanced security
        options.SetDefault().UseSqlServer($"{connectionString};Encrypt=true;TrustServerCertificate=false;");
    }
});
```

### High Availability Configuration

```csharp
services.AddDatabaseManager(options =>
{
    // Primary database with failover
    options.SetDefault().UseSqlServer(
        "Server=primary-db,secondary-db;Database=FluentCMS;Integrated Security=true;Failover Partner=secondary-db;");
    
    // Read-only operations on replica
    options.MapLibrary<IReportingLibraryMarker>().UseSqlServer(
        "Server=replica-db;Database=FluentCMS;ApplicationIntent=ReadOnly;Integrated Security=true;");
});
```

## Performance Considerations

1. **Connection Pooling**: Enabled by default, configure `Max Pool Size` if needed
2. **Multiple Active Result Sets**: Use `MultipleActiveResultSets=true` for complex scenarios
3. **Command Timeout**: Set appropriate `Command Timeout` for long-running operations
4. **Async Operations**: All operations are fully asynchronous for optimal performance
5. **Index Strategy**: Use appropriate indexes for table existence checks

## Security Features

- **Integrated Windows Authentication**: Preferred for internal applications
- **SQL Server Authentication**: With secure password policies
- **Always Encrypted**: Support for column-level encryption
- **Connection Encryption**: SSL/TLS encryption for data in transit
- **Azure Active Directory**: Integration with Azure AD authentication

## Logging

The SQL Server provider includes detailed logging:

- Database creation and existence operations
- Table validation with schema information
- SQL batch execution details
- Connection lifecycle events
- Error conditions with SQL Server error context

Configure logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "FluentCMS.Database.SqlServer": "Information"
    }
  }
}
```

## Troubleshooting

### Common Issues

1. **Database creation failures**: Check permissions on master database
2. **File-attached database issues**: Ensure file paths are accessible and writable
3. **Connection timeout**: Increase `Connection Timeout` for slow networks
4. **Schema not found**: Verify table names include proper schema prefix

### Error Handling

The provider includes comprehensive error handling for:
- Network connectivity issues
- Authentication failures
- Database permission problems
- File system access issues
- SQL execution errors

## Dependencies

This package depends on:
- **FluentCMS.Database.Abstractions**: Core interfaces (via Extensions)
- **FluentCMS.Database.Extensions**: Configuration extensions
- **Microsoft.Data.SqlClient** (6.1.1): SQL Server data provider
- **Microsoft.Extensions.Logging.Abstractions** (9.0.9): Logging infrastructure

## Target Framework

- **.NET 9.0**

## Related Packages

- **FluentCMS.Database.Abstractions**: Core interfaces and abstractions
- **FluentCMS.Database.Extensions**: Configuration and dependency injection
- **FluentCMS.Database.Sqlite**: SQLite provider implementation
