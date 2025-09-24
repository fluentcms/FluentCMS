# FluentCMS.DataSeeding.Abstractions

Core abstractions and interfaces for the FluentCMS data seeding framework. This package contains the essential contracts that enable flexible, extensible database schema validation and data seeding.

## Overview

This package provides the foundational interfaces for implementing:
- **Data Seeders**: Components that populate database with initial data
- **Schema Validators**: Components that validate and create database schemas
- **Conditions**: Components that determine when seeding operations should execute

## Installation

```bash
dotnet add package FluentCMS.DataSeeding.Abstractions
```

*Note: This package contains only interfaces and abstractions. For the full implementation, also install `FluentCMS.DataSeeding`.*

## Core Interfaces

### IDataSeeder

The primary interface for implementing data seeding logic.

```csharp
public interface IDataSeeder
{
    /// <summary>
    /// Execution priority for this seeder. Lower numbers execute first.
    /// Use gaps (10, 20, 30) to allow future insertion without reordering.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Checks if the data this seeder is responsible for already exists.
    /// This enables idempotent seeding operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if data exists and seeding should be skipped, false otherwise</returns>
    Task<bool> HasData(CancellationToken cancellationToken = default);

    /// <summary>
    /// Seeds the data into the database. This method should only be called
    /// if HasData returns false, ensuring idempotent operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    Task SeedData(CancellationToken cancellationToken = default);
}
```

#### Implementation Example

```csharp
public class AdminUserSeeder : IDataSeeder
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AdminUserSeeder> _logger;

    public AdminUserSeeder(IUserRepository userRepository, ILogger<AdminUserSeeder> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public int Priority => 100; // Execute after schema creation (usually 1-99)

    public async Task<bool> HasData(CancellationToken cancellationToken = default)
    {
        // Check if admin user already exists
        return await _userRepository.ExistsAsync("admin", cancellationToken);
    }

    public async Task SeedData(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating default admin user");

        var adminUser = new User
        {
            Username = "admin",
            Email = "admin@company.com",
            PasswordHash = _passwordService.HashPassword("SecurePassword123!"),
            Roles = new[] { "Administrator", "User" },
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.CreateAsync(adminUser, cancellationToken);
        
        _logger.LogInformation("Admin user created successfully");
    }
}
```

### ISchemaValidator

Interface for implementing database schema validation and creation.

```csharp
public interface ISchemaValidator
{
    /// <summary>
    /// Execution priority for this validator. Lower numbers execute first.
    /// Schema validators typically use priorities 1-99, while data seeders use 100+.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Validates whether the required database schema exists and is properly configured.
    /// This should check for tables, indexes, constraints, and other schema elements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if schema is valid, false if CreateSchema should be called</returns>
    Task<bool> ValidateSchema(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the required database schema elements. This method should only be called
    /// if ValidateSchema returns false, ensuring schemas are created only when needed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    Task CreateSchema(CancellationToken cancellationToken = default);
}
```

#### Implementation Example

```csharp
public class UserTableValidator : ISchemaValidator
{
    private readonly IDbConnection _connection;
    private readonly ILogger<UserTableValidator> _logger;

    public UserTableValidator(IDbConnection connection, ILogger<UserTableValidator> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public int Priority => 10; // Execute early in schema validation

    public async Task<bool> ValidateSchema(CancellationToken cancellationToken = default)
    {
        // Check if Users table exists
        var sql = @"
            SELECT COUNT(*) 
            FROM information_schema.tables 
            WHERE table_name = 'Users' AND table_schema = DATABASE()";

        var count = await _connection.QuerySingleAsync<int>(sql);
        return count > 0;
    }

    public async Task CreateSchema(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating Users table");

        var sql = @"
            CREATE TABLE Users (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                Username VARCHAR(50) NOT NULL UNIQUE,
                Email VARCHAR(255) NOT NULL UNIQUE,
                PasswordHash VARCHAR(255) NOT NULL,
                IsActive BOOLEAN NOT NULL DEFAULT TRUE,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NULL ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_username (Username),
                INDEX idx_email (Email)
            )";

        await _connection.ExecuteAsync(sql);
        
        _logger.LogInformation("Users table created successfully");
    }
}
```

### ICondition

Interface for implementing conditional execution logic.

```csharp
public interface ICondition
{
    /// <summary>
    /// Name of the condition for logging purposes
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Evaluates whether seeding operations should execute based on this condition.
    /// Multiple conditions are evaluated with AND logic by default.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if seeding should proceed, false to skip seeding</returns>
    Task<bool> ShouldExecute(CancellationToken cancellationToken = default);
}
```

#### Implementation Examples

**Environment-based Condition:**
```csharp
public class DevelopmentOnlyCondition : ICondition
{
    private readonly IWebHostEnvironment _environment;

    public DevelopmentOnlyCondition(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public string Name => "Development Environment Only";

    public Task<bool> ShouldExecute(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_environment.IsDevelopment());
    }
}
```

**Configuration-based Condition:**
```csharp
public class FeatureFlagCondition : ICondition
{
    private readonly IConfiguration _configuration;
    private readonly string _featureKey;

    public FeatureFlagCondition(IConfiguration configuration, string featureKey)
    {
        _configuration = configuration;
        _featureKey = featureKey;
    }

    public string Name => $"Feature Flag: {_featureKey}";

    public Task<bool> ShouldExecute(CancellationToken cancellationToken = default)
    {
        var enabled = _configuration.GetValue<bool>($"Features:{_featureKey}");
        return Task.FromResult(enabled);
    }
}
```

**Database State Condition:**
```csharp
public class EmptyDatabaseCondition : ICondition
{
    private readonly IDbContext _context;

    public EmptyDatabaseCondition(IDbContext context)
    {
        _context = context;
    }

    public string Name => "Empty Database Check";

    public async Task<bool> ShouldExecute(CancellationToken cancellationToken = default)
    {
        // Only seed if no users exist
        var userCount = await _context.Users.CountAsync(cancellationToken);
        return userCount == 0;
    }
}
```

## Design Patterns

### Priority-based Execution

Use consistent priority ranges for different types of operations:

```csharp
// Schema Validators: 1-99
public class DatabaseValidator : ISchemaValidator 
{ 
    public int Priority => 10; 
}

public class IndexValidator : ISchemaValidator 
{ 
    public int Priority => 20; 
}

// Reference Data Seeders: 100-199
public class CountrySeeder : IDataSeeder 
{ 
    public int Priority => 100; 
}

public class CurrencySeeder : IDataSeeder 
{ 
    public int Priority => 110; 
}

// Master Data Seeders: 200-299
public class RoleSeeder : IDataSeeder 
{ 
    public int Priority => 200; 
}

// User Data Seeders: 300-399
public class AdminUserSeeder : IDataSeeder 
{ 
    public int Priority => 300; 
}

// Transaction Data Seeders: 400+
public class SampleOrderSeeder : IDataSeeder 
{ 
    public int Priority => 400; 
}
```

### Idempotent Operations

Always implement proper existence checks in `HasData()`:

```csharp
public async Task<bool> HasData(CancellationToken cancellationToken = default)
{
    // Check for specific data that indicates this seeder has run
    return await _context.Countries.AnyAsync(c => c.Code == "US", cancellationToken);
}

public async Task SeedData(CancellationToken cancellationToken = default)
{
    // Only seed if HasData returned false
    var countries = new[]
    {
        new Country { Code = "US", Name = "United States" },
        new Country { Code = "CA", Name = "Canada" },
        new Country { Code = "MX", Name = "Mexico" }
    };

    await _context.Countries.AddRangeAsync(countries, cancellationToken);
    await _context.SaveChangesAsync(cancellationToken);
}
```

### Error Handling

Implement robust error handling within your implementations:

```csharp
public async Task SeedData(CancellationToken cancellationToken = default)
{
    try
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        
        // Seed data operations
        await SeedRoles(cancellationToken);
        await SeedPermissions(cancellationToken);
        await LinkRolesAndPermissions(cancellationToken);
        
        await transaction.CommitAsync(cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to seed role data");
        throw; // Re-throw to allow framework error handling
    }
}
```

### Dependency Management

Structure your seeders to handle dependencies properly:

```csharp
public class UserSeeder : IDataSeeder
{
    public int Priority => 300; // After role seeder (200)

    public async Task<bool> HasData(CancellationToken cancellationToken = default)
    {
        // Check for users only, not roles (roles are handled by separate seeder)
        return await _userRepository.AnyAsync(cancellationToken);
    }

    public async Task SeedData(CancellationToken cancellationToken = default)
    {
        // Assume roles exist (seeded by RoleSeeder with lower priority)
        var adminRole = await _roleRepository.GetByNameAsync("Administrator", cancellationToken);
        
        var adminUser = new User
        {
            Username = "admin",
            Roles = new[] { adminRole }
        };

        await _userRepository.CreateAsync(adminUser, cancellationToken);
    }
}
```

## Best Practices

### 1. **Use Descriptive Names**
Make interface implementations self-documenting:

```csharp
// Good
public class AdminUserSeeder : IDataSeeder { }
public class CountryReferenceDataSeeder : IDataSeeder { }
public class ProductionEnvironmentCondition : ICondition { }

// Avoid
public class Seeder1 : IDataSeeder { }
public class DataSeeder : IDataSeeder { }
public class Condition : ICondition { }
```

### 2. **Implement Proper Logging**
Use structured logging for better observability:

```csharp
public async Task SeedData(CancellationToken cancellationToken = default)
{
    _logger.LogInformation("Starting {SeederName} execution", GetType().Name);
    
    var stopwatch = Stopwatch.StartNew();
    
    // Seeding logic here
    var recordsCreated = await CreateRecords(cancellationToken);
    
    stopwatch.Stop();
    
    _logger.LogInformation(
        "Completed {SeederName} in {ElapsedMs}ms, created {RecordCount} records",
        GetType().Name,
        stopwatch.ElapsedMilliseconds,
        recordsCreated);
}
```

### 3. **Handle Cancellation**
Respect cancellation tokens throughout your implementation:

```csharp
public async Task SeedData(CancellationToken cancellationToken = default)
{
    var items = GetItemsToSeed();
    
    foreach (var item in items)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        await _repository.CreateAsync(item, cancellationToken);
    }
}
```

### 4. **Optimize Database Operations**
Use bulk operations when possible:

```csharp
public async Task SeedData(CancellationToken cancellationToken = default)
{
    var countries = CreateCountryList();
    
    // Bulk insert instead of individual operations
    await _context.Countries.AddRangeAsync(countries, cancellationToken);
    await _context.SaveChangesAsync(cancellationToken);
}
```

## Advanced Scenarios

### Conditional Schema Creation

```csharp
public class ConditionalTableValidator : ISchemaValidator
{
    private readonly IFeatureManager _featureManager;

    public int Priority => 50;

    public async Task<bool> ValidateSchema(CancellationToken cancellationToken = default)
    {
        // Only validate if feature is enabled
        if (!await _featureManager.IsEnabledAsync("AdvancedReporting"))
            return true; // Skip validation

        return await CheckTableExists("ReportingData", cancellationToken);
    }

    public async Task CreateSchema(CancellationToken cancellationToken = default)
    {
        // Create table only if feature is enabled
        if (await _featureManager.IsEnabledAsync("AdvancedReporting"))
        {
            await CreateReportingTable(cancellationToken);
        }
    }
}
```

### Multi-Environment Seeders

```csharp
public class EnvironmentSpecificSeeder : IDataSeeder
{
    private readonly IWebHostEnvironment _environment;

    public int Priority => 200;

    public async Task SeedData(CancellationToken cancellationToken = default)
    {
        if (_environment.IsDevelopment())
        {
            await SeedDevelopmentData(cancellationToken);
        }
        else if (_environment.IsStaging())
        {
            await SeedStagingData(cancellationToken);
        }
        else if (_environment.IsProduction())
        {
            await SeedProductionData(cancellationToken);
        }
    }
}
```
