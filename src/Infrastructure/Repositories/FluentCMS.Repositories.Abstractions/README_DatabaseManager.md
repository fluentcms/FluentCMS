# FluentCMS Database Manager

The Database Manager provides a centralized configuration system for managing database connections across multiple libraries in FluentCMS. This allows libraries to remain database-agnostic while giving main applications full control over which areas use which database providers.

## Key Concepts

### Database Areas
Each library defines one or more **database area markers** - interfaces that inherit from `IDatabaseArea`. These markers represent logical groupings of data that can be stored in separate databases.

```csharp
// In your library
public interface ITodoDatabaseMarker : IDatabaseArea
{
}
```

### Library Registration
Libraries register their DbContexts using `AddDataContextForArea<TArea, TContext>()`:

```csharp
// In your library's service registration
services.AddDataContextForArea<ITodoDatabaseMarker, TodoDbContext>(options =>
{
    options.ConfigureWarnings(warnings => warnings.Throw());
    options.EnableSensitiveDataLogging();
});

// Register your business services separately
services.AddScoped<ITodoRepository, TodoRepository>();
services.AddScoped<ITodoService, TodoService>();
```

### Main Application Configuration
The main application configures database providers for all areas using `AddDatabaseManager()`:

```csharp
// In your main application's startup
services.AddDatabaseManager(options =>
{
    // Default database for any area not explicitly configured
    options.Default()
        .UseSqlite("Data Source=default.db", sqliteOptions => {})
        .EnableDataSeeding(seedingOptions =>
        {
            seedingOptions.IgnoreExceptions = false;
            seedingOptions.Conditions.Add(new EnvironmentCondition(
                environment,
                e => e.IsDevelopment()));
        });

    // Specific database for Todo library
    options.For<ITodoDatabaseMarker>()
        .UseSqlServer("Server=localhost;Database=TodoDb;Trusted_Connection=True;", sqlServerOptions => {})
        .EnableDataSeeding(seedingOptions =>
        {
            seedingOptions.IgnoreExceptions = false;
            seedingOptions.Conditions.Add(new EnvironmentCondition(
                environment,
                e => e.IsDevelopment()));
        });
});
```

## Architecture

### Registration Flow

1. **Library Phase**: Libraries call `AddDataContextForArea<TArea, TContext>()` to register their areas and DbContexts
2. **Configuration Phase**: Main application calls `AddDatabaseManager()` to configure database providers
3. **Resolution Phase**: Database Manager processes all registrations and configures DbContexts with appropriate providers

### Configuration Merging

The system merges configurations in this order:
1. **Library DbContext Options** - Applied first (base configuration from library)
2. **Database Provider Configuration** - Applied second (provider-specific setup)
3. **Main Application Provider Options** - Applied last (environment-specific overrides)

## Database Providers

### Built-in Providers
- **Mock Provider**: In-memory database for testing (included in abstractions)

### External Providers (Separate Packages)
- **SQLite**: `FluentCMS.Repositories.Sqlite` package
- **SQL Server**: `FluentCMS.Repositories.SqlServer` package
- **Custom**: Implement `IDatabaseProvider` interface

### Creating Custom Providers

```csharp
public class MyCustomProvider : IDatabaseProvider
{
    public string ProviderName => "MyCustom";

    public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString)
    {
        // Configure your database provider
        optionsBuilder.UseMyDatabase(connectionString);
    }
}

// Extension method for fluent API
public static class MyCustomProviderExtensions
{
    public static DatabaseAreaConfiguration<TArea> UseMyCustom<TArea>(
        this DatabaseAreaConfiguration<TArea> configuration,
        string connectionString,
        Action<DbContextOptionsBuilder>? options = null)
        where TArea : class, IDatabaseArea
    {
        var provider = new MyCustomProvider();
        return configuration.UseProvider(provider, connectionString, options);
    }
}
```

## Usage Examples

See `Examples/AddDatabaseManagerDemo.cs` for complete working examples including:
- Basic configuration with mock providers
- Real database provider configuration
- DbContext options configuration
- Data seeding setup

## Migration from Legacy Approach

### Before (Per-Library Registration)
```csharp
// Each library had its own registration method
services.AddTodoServices();
services.AddIdentityServices();
services.AddCrmServices();
```

### After (Centralized Configuration)
```csharp
// Libraries register their areas
services.AddDataContextForArea<ITodoDatabaseMarker, TodoDbContext>();
services.AddTodoServices(); // Business services only

// Main application configures all databases
services.AddDatabaseManager(options => { /* ... */ });
```

## Benefits

- **Database-Agnostic Libraries**: Libraries don't need to know about specific database providers
- **Centralized Configuration**: All database settings in one place
- **Flexible Assignment**: Different areas can use different databases
- **Better Testing**: Easy to use mock providers for testing
- **Clear Separation**: Database concerns separated from business logic
- **Performance**: No assembly scanning or auto-discovery overhead
