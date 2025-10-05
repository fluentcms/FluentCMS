# FluentCMS.Repositories.Abstractions

A comprehensive abstraction library for repository patterns in .NET 9.0, providing unified data access interfaces that work seamlessly with both Entity Framework Core.

## Overview

This library provides abstractions for building scalable, testable, and maintainable data access layers in .NET applications. It offers a consistent API across different database providers while maintaining the unique capabilities of each platform.

## Key Features

- **Unified Repository API**: Single interface for CRUD operations across different database providers
- **Fluent Query Specification**: LINQ-style querying with pagination support
- **Entity Interfaces**: Standardized entity contracts for audit trails and versioning
- **Database Areas**: Type-safe database scope definitions
- **Data Initialization**: Robust schema validation and data seeding framework
- **Async-First Design**: All operations are async with proper `CancellationToken` support
- **Generic Type Safety**: Full generic type safety while maintaining flexibility

## Installation

```bash
dotnet add package FluentCMS.Repositories.Abstractions
```

## Core Interfaces

### Entity Interfaces

```csharp
// Basic entity interface
public interface IEntity
{
    Guid Id { get; set; }
}

// Auditable entity with versioning
public interface IAuditableEntity : IEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? CreatedBy { get; set; }
    string? UpdatedBy { get; set; }
    int Version { get; set; }
}
```

### Repository Interface

```csharp
public interface IRepository<TEntity> where TEntity : class
{
    // Core CRUD operations
    Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity> Remove(TEntity entity, CancellationToken cancellationToken = default);

    // Single entry point for all queries
    IQuerySpecification<TEntity> Query();
}
```

### Query Specification Interface

The `IQuerySpecification<TEntity>` interface provides a comprehensive LINQ-style querying API:

```csharp
public interface IQuerySpecification<TEntity> where TEntity : class
{
    // Filtering and projection
    IQuerySpecification<TEntity> Where(Expression<Func<TEntity, bool>> predicate);
    IQuerySpecification<TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector);
    IQuerySpecification<TEntity> Distinct();

    // Ordering operations
    IQuerySpecification<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector);
    IQuerySpecification<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector);

    // Terminal operations
    Task<TEntity?> SingleOrDefault(CancellationToken cancellationToken = default);
    Task<List<TEntity>> ToList(CancellationToken cancellationToken = default);
    Task<TEntity[]> ToArray(CancellationToken cancellationToken = default);

    // Aggregate operations
    Task<int> Count(CancellationToken cancellationToken = default);
    Task<bool> Any(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    // Pagination support
    Task<IPagedResult<TEntity>> ToPagedResult(int page, int pageSize, CancellationToken cancellationToken = default);
}
```

### Pagination Interface

```csharp
public interface IPagedResult<T>
{
    IEnumerable<T> Items { get; }
    long TotalCount { get; }
    int Page { get; }
    int PageSize { get; }
    int TotalPages { get; }
    bool HasNextPage { get; }
    bool HasPreviousPage { get; }
}
```

## Database Areas

Database areas provide type-safe database scope definitions:

```csharp
// Marker interface for database scope
public interface IDatabaseArea
{
    // Marker interface - no methods required
}

// Example implementation
public interface IBlogDatabaseArea : IDatabaseArea { }
public interface IAdminDatabaseArea : IDatabaseArea { }
```

Use database areas to group entities by their target database, enabling cleaner dependency injection and type safety.

## Data Initialization Framework

The library includes a comprehensive data initialization system for schema validation and data seeding.

### Schema Validation

```csharp
public interface ISchemaValidator
{
    int Priority { get; }
    Task<bool> ValidateSchema(CancellationToken cancellationToken = default);
    Task CreateSchema(CancellationToken cancellationToken = default);
}

public class SchemaValidationOptions
{
    public List<IDbInitializationCondition> Conditions { get; set; } = [];
    public bool IgnoreExceptions { get; set; } = false;
}
```

### Data Seeding

```csharp
public interface IDataSeeder
{
    int Priority { get; }
    Task<bool> ShouldSeed(CancellationToken cancellationToken = default);
    Task SeedData(CancellationToken cancellationToken = default);
}

public class DataSeedingOptions
{
    public List<IDbInitializationCondition> Conditions { get; set; } = [];
    public bool IgnoreExceptions { get; set; } = false;
}
```

### Initialization Conditions

Conditions determine when initialization operations run:

```csharp
public interface IDbInitializationCondition
{
    string Name { get; }
    Task<bool> ShouldExecute(CancellationToken cancellationToken = default);
}

// Built-in condition types
public class ConfigurationCondition : IDbInitializationCondition { }
public class EnvironmentCondition : IDbInitializationCondition { }
public class CompositeCondition : IDbInitializationCondition { }
```

## Usage Examples

### Basic CRUD Operations

```csharp
public class UserService
{
    private readonly IRepository<User> _userRepository;

    public UserService(IRepository<User> userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<User> CreateUser(User user)
    {
        return await _userRepository.Add(user);
    }

    public async Task<List<User>> GetActiveUsers()
    {
        return await _userRepository.Query()
            .Where(u => u.IsActive)
            .OrderBy(u => u.CreatedAt)
            .ToList();
    }

    public async Task<User?> GetUserByEmail(string email)
    {
        return await _userRepository.Query()
            .Where(u => u.Email == email)
            .SingleOrDefault();
    }
}
```

### Pagination

```csharp
public async Task<IPagedResult<User>> GetUsersPaged(int page, int pageSize)
{
    return await _userRepository.Query()
        .Where(u => u.IsActive)
        .OrderBy(u => u.CreatedAt)
        .ToPagedResult(page, pageSize);
}
```

### Aggregation

```csharp
public async Task<bool> HasActiveUsers()
{
    return await _userRepository.Query()
        .Any(u => u.IsActive);
}

public async Task<int> GetActiveUserCount()
{
    return await _userRepository.Query()
        .Count(u => u.IsActive);
}
```

### Data Seeding Setup

```csharp
// Register seeders with DI
services.AddTransient<IDataSeeder, UserSeeder>();

// Configure seeding options
services.Configure<DataSeedingOptions>(options =>
{
    options.Conditions.Add(new EnvironmentCondition(env =>
        env.IsDevelopment() || env.IsStaging()));

    options.IgnoreExceptions = false;
});
```

## Architecture

The library follows these architectural principles:

- **Single Responsibility**: Each interface has one clear purpose
- **Interface Segregation**: Clients depend only on the interfaces they need
- **Dependency Inversion**: High-level modules don't depend on low-level modules
- **Async by Default**: All operations support cancellation and async execution
- **Type Safety**: Full generic constraints and validated operations
- **Flexibility**: Extensible design allows custom implementations

## Supported Providers

The abstractions are designed to work with concrete implementations for:

- Entity Framework Core 
- In-memory implementations for testing

## Best Practices

### Entity Design
- Implement `IEntity` or `IAuditableEntity` based on your auditing requirements
- Use `Guid` for primary keys to ensure compatibility across providers
- Consider database areas for multi-database applications

### Repository Usage
- Use the query specification pattern for complex queries
- Prefer async operations with proper cancellation tokens
- Keep business logic in application services, repositories for data access

### Data Initialization
- Use priority ordering for schema validators
- Use priority ordering for data seeders
- Always check `ShouldSeed()` before calling `SeedData()`
- Use appropriate conditions to avoid seeding in production unintentionally

## Contributing

This is an abstractions library - implement concrete repository classes that inherit from these interfaces for your specific database providers.

For concrete implementations with Entity Framework Core, see the sister packages in the FluentCMS ecosystem.

## License

Licensed under the MIT License. See LICENSE file for details.
