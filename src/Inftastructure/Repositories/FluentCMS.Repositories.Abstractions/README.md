# FluentCMS Repositories Abstractions - Specification Pattern

This library provides a comprehensive, database-agnostic specification pattern implementation for FluentCMS that mimics the powerful querying capabilities of Entity Framework's `AsQueryable()` while remaining completely independent of any specific data provider.

## Overview

The specification pattern implementation offers:

- **Full LINQ Support**: Where, OrderBy, ThenBy, Skip, Take, GroupBy, Select, Distinct, etc.
- **Database Agnostic**: Works with any IQueryable provider (EF Core, MongoDB, Dapper, etc.)
- **Type Safety**: Complete compile-time checking with full IntelliSense support
- **Composable**: Combine specifications with AND/OR/NOT logic
- **Performance**: Expression trees are compiled and executed efficiently
- **Testable**: Easy to unit test business logic with mock specifications

## Key Components

### Core Interfaces

- **`ISpecification<T>`**: Base interface for all specifications
- **`ICompositeSpecification<T>`**: Enables combining specifications with logical operators
- **`IQuerySpecification<T>`**: Fluent query building interface (like LINQ)
- **`IProjectionSpecification<TResult>`**: Handles projections to any result type

### Implementation Classes

- **`Specification<T>`**: Abstract base class for custom specifications
- **`ExpressionSpecification<T>`**: Simple expression-based specification
- **`QuerySpecification<T>`**: Fluent query builder implementation
- **`ProjectionSpecification<TSource, TResult>`**: Projection handling

### Repository Extensions

The `IRepository<TEntity>` interface has been extended with:

```csharp
// Fluent specification builder
IQuerySpecification<TEntity> Query();

// Specification-based operations
Task<IEnumerable<TEntity>> Find(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
Task<TEntity?> FindFirst(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
Task<int> Count(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);
Task<bool> Any(ISpecification<TEntity> specification, CancellationToken cancellationToken = default);

// Projection support
Task<IEnumerable<TResult>> FindProjected<TResult>(IProjectionSpecification<TResult> projection, CancellationToken cancellationToken = default);

// Basic aggregation
Task<decimal> Sum(ISpecification<TEntity> specification, Expression<Func<TEntity, decimal>> selector, CancellationToken cancellationToken = default);
```

## Usage Examples

### 1. Fluent Specification Builder
```csharp
var userQuery = repository.Query()
    .Where(u => u.Name.Contains("John"))
    .Where(u => u.IsActive)
    .OrderBy(u => u.Department)
    .ThenByDescending(u => u.CreatedDate)
    .Skip(10)
    .Take(20);

var users = await repository.Find(userQuery.ToSpecification());
```

### 2. Reusable Expression Specifications
```csharp
var activeUsersSpec = new ExpressionSpecification<User>(u => u.IsActive);
var recentUsersSpec = new ExpressionSpecification<User>(u => u.CreatedDate > DateTime.Now.AddDays(-30));

// Compose specifications
var activeRecentUsersSpec = activeUsersSpec.And(recentUsersSpec);
var users = await repository.Find(activeRecentUsersSpec);
```

### 3. Projections
```csharp
var userSummaries = repository.Query()
    .Where(u => u.IsActive)
    .Select(u => new UserSummaryDto { Id = u.Id, Name = u.Name, Email = u.Email })
    .OrderBy(s => s.Name);

var results = await repository.FindProjected(userSummaries);
```

### 4. Grouping with Aggregations
```csharp
// Group users by department and count them
var departmentCounts = repository.Query()
    .Where(u => u.IsActive)
    .GroupBy(u => u.Department)
    .Select(g => new { Department = g.Key, Count = g.Count() })
    .OrderByDescending(x => x.Count);

var results = await repository.FindProjected(departmentCounts);
```

### 5. Pagination with Total Count
```csharp
var searchSpec = new ExpressionSpecification<User>(u => u.Name.Contains("John"));
var pagedResult = await repository.FindPaged(searchSpec, page: 1, pageSize: 20);

Console.WriteLine($"Found {pagedResult.TotalCount} users, showing page {pagedResult.Page} of {pagedResult.TotalPages}");
```

## Helper Classes and Extensions

### Common Specifications
```csharp
var userById = CommonSpecifications.ById<User>(userId);
var usersByIds = CommonSpecifications.ByIds<User>(userIds);
var allUsers = CommonSpecifications.All<User>();
```

### Extension Methods
```csharp
// Pagination helper
var pagedQuery = query.Paginate(page: 1, pageSize: 20);

// Convenience methods
var exists = await repository.Exists(specification);
var user = await repository.GetFirst(specification); // Throws if not found
var singleUser = await repository.GetSingle(specification); // Throws if not found or multiple
```

### Pagination Support
```csharp
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; }
    public bool HasNextPage { get; }
    public bool HasPreviousPage { get; }
}
```

## Files Included

- **`ISpecification.cs`**: Core specification interfaces
- **`Specification.cs`**: Base implementation classes and composition logic
- **`QuerySpecification.cs`**: Fluent query builder implementation
- **`IRepository.cs`**: Extended repository interface with specification support
- **`SpecificationExtensions.cs`**: Helper methods, common specifications, and extensions
- **`SPECIFICATION_USAGE_EXAMPLES.md`**: Comprehensive usage examples and documentation

## Design Principles

1. **Database Agnostic**: No dependencies on specific ORM or database technologies
2. **LINQ Compatible**: Familiar syntax for developers already using LINQ
3. **Type Safe**: Full compile-time checking and IntelliSense support
4. **Immutable**: Specifications are immutable; operations return new instances
5. **Composable**: Specifications can be combined and reused across different queries
6. **Testable**: Easy to unit test business logic with mock specifications
7. **Performance**: Expression trees are compiled and executed efficiently by query providers

## Project Conventions Followed

- No "Async" suffix on async method names
- CancellationToken parameters with default values on all async methods
- Inline comments instead of XML documentation
- Database-agnostic design supporting any IQueryable provider

## Next Steps for Implementation

To use this specification pattern, you'll need to:

1. **Implement the repository interface** in your data layer (EF Core, MongoDB, etc.)
2. **Implement the specification execution logic** in your repository implementations
3. **Add the IEntity interface** to your entity base classes
4. **Configure dependency injection** for your repositories

The abstractions provided here give you a complete foundation for implementing a powerful, flexible, and maintainable repository pattern with full LINQ support across any data provider.
