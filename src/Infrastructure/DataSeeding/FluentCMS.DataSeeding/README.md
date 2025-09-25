# FluentCMS.DataSeeding

A flexible and powerful data seeding framework for .NET applications that provides automated database schema validation and data initialization during application startup.

## Features

- **Priority-based Execution**: Execute seeders and validators in a defined order
- **Conditional Seeding**: Configure conditions that determine when seeding should occur
- **Schema Validation**: Automatically validate and create database schemas before seeding
- **Idempotent Operations**: Built-in checks to prevent duplicate data seeding
- **Parallel Processing**: Optimized condition evaluation for better performance
- **Timeout Support**: Configurable timeouts to prevent hanging operations
- **Error Handling**: Flexible exception handling with optional error suppression
- **Dependency Injection**: Full integration with Microsoft DI container

## Installation

```bash
dotnet add package FluentCMS.DataSeeding
dotnet add package FluentCMS.DataSeeding.Abstractions
```

## Quick Start

### 1. Create a Data Seeder

```csharp
public class UserSeeder : IDataSeeder
{
    private readonly IUserRepository _userRepository;

    public UserSeeder(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public int Priority => 100; // Lower numbers execute first

    public async Task<bool> HasData(CancellationToken cancellationToken = default)
    {
        return await _userRepository.AnyAsync(cancellationToken);
    }

    public async Task SeedData(CancellationToken cancellationToken = default)
    {
        var adminUser = new User
        {
            Username = "admin",
            Email = "admin@example.com",
            Role = "Administrator"
        };

        await _userRepository.CreateAsync(adminUser, cancellationToken);
    }
}
```

### 2. Create a Schema Validator

```csharp
public class DatabaseSchemaValidator : ISchemaValidator
{
    private readonly IDbContext _context;

    public DatabaseSchemaValidator(IDbContext context)
    {
        _context = context;
    }

    public int Priority => 10; // Execute before data seeders

    public async Task<bool> ValidateSchema(CancellationToken cancellationToken = default)
    {
        // Check if required tables exist
        return await _context.Database.CanConnectAsync(cancellationToken);
    }

    public async Task CreateSchema(CancellationToken cancellationToken = default)
    {
        // Create database schema
        await _context.Database.EnsureCreatedAsync(cancellationToken);
    }
}
```

### 3. Configure Services

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // Register your repositories and services
    services.AddDbContext<ApplicationDbContext>();
    services.AddScoped<IUserRepository, UserRepository>();

    // Register schema validators
    services.AddSchemaValidator<DatabaseSchemaValidator>();

    // Register data seeders
    services.AddDataSeeder<UserSeeder>();

    // Configure seeding options
    services.AddDataSeeders(options =>
    {
        options.IgnoreExceptions = false; // Fail fast on errors
        options.Timeout = TimeSpan.FromMinutes(10); // Custom timeout
        
        // Add conditions
        options.Conditions.Add(new EnvironmentCondition(env, e => e.IsDevelopment()));
    });

    services.AddSchemaValidators(options =>
    {
        options.IgnoreExceptions = false;
        options.Timeout = TimeSpan.FromMinutes(5);
        
        // Only run schema validation in Development
        options.Conditions.Add(new EnvironmentCondition(env, e => e.IsDevelopment()));
    });
}
```

## Advanced Configuration

### Conditional Seeding

Control when seeding occurs using built-in or custom conditions:

```csharp
services.AddDataSeeders(options =>
{
    // Environment-based conditions
    options.Conditions.Add(new EnvironmentCondition(env, e => e.IsDevelopment()));
    
    // Configuration-based conditions
    options.Conditions.Add(new ConfigurationCondition(config, "SeedData", "true"));
    
    // Complex composite conditions
    var devOrStaging = new CompositeCondition(false, // OR logic
        new EnvironmentCondition(env, e => e.IsDevelopment()),
        new EnvironmentCondition(env, e => e.IsStaging())
    );
    options.Conditions.Add(devOrStaging);
});
```

### Custom Conditions

Create your own conditions by implementing `ICondition`:

```csharp
public class DatabaseEmptyCondition : ICondition
{
    private readonly IDbContext _context;

    public DatabaseEmptyCondition(IDbContext context)
    {
        _context = context;
    }

    public string Name => "Database Empty Check";

    public async Task<bool> ShouldExecute(CancellationToken cancellationToken = default)
    {
        // Only seed if database is empty
        var tableCount = await _context.Users.CountAsync(cancellationToken);
        return tableCount == 0;
    }
}
```

### Priority-based Execution

Use priorities to control execution order:

```csharp
public class ReferenceDataSeeder : IDataSeeder
{
    public int Priority => 10; // Execute first
    // ... implementation
}

public class UserSeeder : IDataSeeder
{
    public int Priority => 20; // Execute after reference data
    // ... implementation
}

public class TransactionSeeder : IDataSeeder
{
    public int Priority => 30; // Execute last
    // ... implementation
}
```

### Error Handling

Configure how the system handles errors:

```csharp
services.AddDataSeeders(options =>
{
    // Continue seeding even if individual seeders fail
    options.IgnoreExceptions = true;
});

// Or handle errors in your seeder
public async Task SeedData(CancellationToken cancellationToken = default)
{
    try
    {
        await SeedUsers(cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to seed users, but continuing with other data");
        // Optionally seed fallback data
    }
}
```

### Timeout Configuration

Set timeouts to prevent hanging operations:

```csharp
services.AddDataSeeders(options =>
{
    options.Timeout = TimeSpan.FromMinutes(15); // Long-running seed operations
});

services.AddSchemaValidators(options =>
{
    options.Timeout = TimeSpan.FromMinutes(2); // Quick schema checks
});
```

## Built-in Conditions

### EnvironmentCondition

Execute seeding based on hosting environment:

```csharp
// Development only
new EnvironmentCondition(env, e => e.IsDevelopment())

// Production only
new EnvironmentCondition(env, e => e.IsProduction())

// Development or Staging
new EnvironmentCondition(env, e => e.IsDevelopment() || e.IsStaging())
```

### ConfigurationCondition

Execute seeding based on configuration values:

```csharp
// Check app settings
new ConfigurationCondition(config, "Features:SeedData", "enabled")

// Check connection strings
new ConfigurationCondition(config, "ConnectionStrings:DefaultConnection", "Server=localhost")
```

### CompositeCondition

Combine multiple conditions with AND/OR logic:

```csharp
// All conditions must be true (AND)
var allConditions = new CompositeCondition(true,
    new EnvironmentCondition(env, e => e.IsDevelopment()),
    new ConfigurationCondition(config, "SeedData", "true")
);

// Any condition must be true (OR)
var anyCondition = new CompositeCondition(false,
    new EnvironmentCondition(env, e => e.IsDevelopment()),
    new EnvironmentCondition(env, e => e.IsStaging())
);
```

## Performance Features

### Cached Ordering
Seeders and validators are ordered by priority once during service initialization, eliminating repeated sorting operations.

### Parallel Condition Evaluation
Multiple conditions are evaluated concurrently using `Task.WhenAll()` for better performance.

### Optimized Execution
The framework uses efficient algorithms and caching to minimize startup overhead.

## Best Practices

### 1. Use Meaningful Priorities
Leave gaps between priority numbers to allow for future insertion:

```csharp
public class SchemaValidator : ISchemaValidator
{
    public int Priority => 10; // Schema creation
}

public class ReferenceDataSeeder : IDataSeeder
{
    public int Priority => 100; // Reference data
}

public class UserSeeder : IDataSeeder
{
    public int Priority => 200; // Users
}
```

### 2. Implement Proper HasData Checks
Always implement efficient existence checks:

```csharp
public async Task<bool> HasData(CancellationToken cancellationToken = default)
{
    // Use efficient existence check
    return await _context.Users.AnyAsync(u => u.Username == "admin", cancellationToken);
}
```

### 3. Use Transactions for Data Integrity
Wrap seeding operations in transactions:

```csharp
public async Task SeedData(CancellationToken cancellationToken = default)
{
    using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    
    try
    {
        await SeedUsers(cancellationToken);
        await SeedRoles(cancellationToken);
        
        await transaction.CommitAsync(cancellationToken);
    }
    catch
    {
        await transaction.RollbackAsync(cancellationToken);
        throw;
    }
}
```

### 4. Log Seeding Operations
Use structured logging for better monitoring:

```csharp
public async Task SeedData(CancellationToken cancellationToken = default)
{
    _logger.LogInformation("Starting user seeding process");
    
    var users = await CreateUsers(cancellationToken);
    
    _logger.LogInformation("Seeded {UserCount} users successfully", users.Count);
}
```

## API Reference

### IDataSeeder Interface

```csharp
public interface IDataSeeder
{
    int Priority { get; }
    Task<bool> HasData(CancellationToken cancellationToken = default);
    Task SeedData(CancellationToken cancellationToken = default);
}
```

### ISchemaValidator Interface

```csharp
public interface ISchemaValidator
{
    int Priority { get; }
    Task<bool> ValidateSchema(CancellationToken cancellationToken = default);
    Task CreateSchema(CancellationToken cancellationToken = default);
}
```

### ICondition Interface

```csharp
public interface ICondition
{
    string Name { get; }
    Task<bool> ShouldExecute(CancellationToken cancellationToken = default);
}
```

### Configuration Options

```csharp
public class DataSeederOptions
{
    public List<ICondition> Conditions { get; set; } = [];
    public bool IgnoreExceptions { get; set; } = false;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}

public class SchemaValidatorOptions
{
    public List<ICondition> Conditions { get; set; } = [];
    public bool IgnoreExceptions { get; set; } = false;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}
```

## Troubleshooting

### Common Issues

**Seeding doesn't run:**
- Check that conditions are met
- Verify services are properly registered
- Ensure `HasData()` returns `false` when seeding is needed

**Timeout errors:**
- Increase timeout values in options
- Optimize database operations
- Check database connectivity

**Dependency injection errors:**
- Ensure all dependencies are registered before seeding services
- Use scoped services for database operations

### Debugging

Enable detailed logging:

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});
```
