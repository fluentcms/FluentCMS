# FluentCMS Repositories - Multi-Database Support

A flexible, extensible repository abstraction layer that supports multiple database providers (EF Core, MongoDB) with per-area configuration across different libraries.

## 🏗️ Architecture Overview

### Core Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                        │
│                        (Libraries)                          │
│              ┌─────────────────┬─────────────────┐          │
│          Todo│         Blog│         Log │... Others        │
│              └─────────────────┴─────────────────┘          │
└───────────────────────┬─────────────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────────────┐
│                Database Manager                             │
│  ├─ Area Registry (Type → Config)                           │
│  ├─ Default Config                                          │
│  └─ Context Factory (resolves per library area)             │
└───────────────────────┬─────────────────────────────────────┘
                        │
┌───────────────────────▼────────────────────────────────────┐
│            Provider Packages Layer                         │
│  ├─ ≤ FluentCMS.Repositories.EntityFramework > ──┐         │
│  │  └─ EfDataContext                           │      │
│  │    └─ EfEntitySet                            │      │
│  ├─ ≤ FluentCMS.Repositories.Sqlite > ──────────┼─────┐│
│  │  └─ UseSqlite() → EfDataContext with Sqlite   │     ││
│  ├─ ≤ FluentCMS.Repositories.SqlServer > ────────┼─────┐│
│  │  └─ UseSqlServer() → EfDataContext with SQL    │     ││
│  └─ ≤ FluentCMS.Repositories.MongoDB > ──────────┘─────┘├─┘
│     └─ UseMongoDB() → MongoDataContext           └─────┘
└─────────────────────────────────────────────────────────────┘
```

### Package Structure (After Split)

```
FluentCMS.Repositories/
├── Abstractions/                    # Core interfaces and manager
│   ├── IDatabaseArea.cs
│   ├── IDataContext.cs
│   ├── IRepository.cs
│   ├── BaseRepository.cs
│   ├── DatabaseManager.cs       # Registration and resolution
│   └── ServiceCollectionExtensions.cs
├── EntityFramework/               # Base EF implementation
│   ├── EfDataContext.cs
│   └── EfEntitySet.cs
├── Sqlite/                       # Sqlite provider extensions
│   └── ServiceCollectionExtensions.cs
├── SqlServer/                    # SQL Server provider extensions
│   └── ServiceCollectionExtensions.cs
└── MongoDB/                      # MongoDB provider extensions
    ├── MongoDataContext.cs
    ├── MongoEntitySet.cs
    └── ServiceCollectionExtensions.cs
```

## 📋 Key Concepts

### Database Areas
- Each library defines its own database area via marker interfaces
- Example: `ITodoDatabaseMarker`, `ILogDatabaseMarker`
- Isolates library data concerns and allows per-area configuration

### Default vs Specific Configuration
- **Default**: Applied to libraries without specific config
- **Specific**: Overrides default for particular areas
- Fallback ensures all areas are covered

### Provider Extensions
- Each database provider is in its own package
- Supports EF Core (Sqlite, SqlServer) and MongoDB
- Extension methods add `UseProvider()` fluent APIs

## 🚀 Quick Start

### 1. Define Your Database Area

```csharp
// In your library project (e.g., TodoLibrary)
public interface ITodoDatabaseMarker : IDatabaseArea {}

public interface ITodoDataContext : IDataContext<ITodoDatabaseMarker> {}
```

### 2. Register Services

```csharp
// In Program.cs or Startup.cs
builder.Services.AddDatabaseManager(options =>
{
    // Default for most libraries
    options.Default()
        .UseSqlite("Data Source=default.db");

    // Custom for Todo library
    options.For<ITodoDatabaseMarker>()
        .UseSqlServer("Server=localhost;Database=TodoDb;Trusted_Connection=True;", sqlOpts =>
            sqlOpts.EnableRetryOnFailure(maxRetryCount: 3)));

    // Custom MongoDB for Log library
    options.For<ILogDatabaseMarker>()
        .UseMongoDB("mongodb://localhost:27017", "LogsDb");
});
```

### 3. Register Library Context

```csharp
// In your library's DI registration
services.AddDataContextForArea<ITodoDatabaseMarker>();
```

### 4. Use in Services

```csharp
// Injected into your TodoService
public class TodoService(ITodoDataContext dataContext)
{
    public async Task<TodoItem> CreateTodo(string title, string description)
    {
        // Automatically uses SQL Server configured for ITodoDatabaseMarker
        var repository = new TodoRepository(dataContext);
        return await repository.Add(new TodoItem { Title = title, Description = description });
    }
}
```

## 📖 Detailed Usage Examples

### Basic Configuration

```csharp
builder.Services.AddDatabaseManager(options =>
{
    // Single database for all
    options.Default()
        .UseSqlite("Data Source=myapp.db");
});
```

### Multiple Libraries, Different Databases

```csharp
builder.Services.AddDatabaseManager(options =>
{
    // Blog uses PostgreSQL (with EF Core driver)
    options.For<IBlogDatabaseMarker>()
        .UseNpgsql("Server=localhost;Database=blog;User Id=user;Password=pass;");

    // Analytics uses MongoDB
    options.For<IAnalyticsDatabaseMarker>()
        .UseMongoDB("mongodb://atlas-connection-string", "analytics");

    // All others (Todo, Auth, Settings) use default Sqlite
    options.Default()
        .UseSqlite("Data Source=default.db", sqliteOpts =>
            sqliteOpts.UseNetTopologySuite().UseRowNumberForPaging());
});
```

### Advanced EF Configuration

```csharp
builder.Services.AddDatabaseManager(options =>
{
    options.For<IHighPerformanceDatabaseMarker>()
        .UseSqlServer(
            "Server=production-cluster;Database=hpdb;Trusted_Connection=True;",
            sqlOpts =>
            {
                sqlOpts.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);
                sqlOpts.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
                sqlOpts.UseTransactionIsolationLevel(IsolationLevel.ReadCommitted);
            });
});
```

### MongoDB Configuration

```csharp
builder.Services.AddDatabaseManager(options =>
{
    options.For<ILogDatabaseMarker>()
        .UseMongoDB(
            "mongodb://username:password@cluster.mongodb.net/?retryWrites=true&w=majority",
            "logs_database");
});
```

## 🏛️ Architecture Benefits

### ✅ Separation of Concerns
- Each library defines its data area independently
- Provider packages are plug-and-play
- Core abstractions remain database-agnostic

### ✅ Flexible Database Assignment
- One application = multiple databases seamlessly
- Add new libraries without changing existing config
- Override per library as needed

### ✅ Dependency Isolation
- Provider packages only include their NuGet dependencies
- No runtime burden from unused providers
- Libraries only depend on abstractions

### ✅ Developer Experience
- Fluent API for configuration
- Type-safe library areas
- IntelliSense support for provider options
- Clear separation in codebase

### ✅ Extensible Design
- Easy to add new database providers
- Custom configuration per provider
- Modular package structure

## 🔧 Configuration Reference

### Default Configuration
```csharp
options.Default()
    .UseSqlite(string connectionString, Action<SqliteDbContextOptionsBuilder>? optionsAction = null)
    .UseSqlServer(string connectionString, Action<SqlServerDbContextOptionsBuilder>? optionsAction = null)
    .UseMongoDB(string connectionString, string databaseName)
```

### Library-Specific Configuration
```csharp
options.For<TLibraryMarker>()
    // Same provider options as above
```

## 🛠️ Creating New Libraries

1. **Define Marker Interface**:
   ```csharp
   public interface IMyLibraryDatabase : IDatabaseArea {}
   ```

2. **Define Context Interface**:
   ```csharp
   public interface IMyLibraryContext : IDataContext<IMyLibraryDatabase> {}
   ```

3. **Create Repositories**:
   ```csharp
   public interface IMyRepository : IRepository<MyEntity> {}

   internal class MyRepository(MyEntity context) : BaseRepository<MyEntity, IMyLibraryContext>(context), IMyRepository {}
   ```

4. **Register in Library Startup**:
   ```csharp
   services.AddDataContextForArea<IMyLibraryDatabase>();
   ```

5. **Configure in Application**:
   ```csharp
   options.For<IMyLibraryDatabase>().UseMongoDB("connection-string", "db-name");
   ```

The multi-database manager will automatically resolve the correct data context for your library!
