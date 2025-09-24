# Specification Pattern Usage Examples

This document demonstrates how to use the comprehensive specification pattern implemented in FluentCMS.Repositories.Abstractions.

## Overview

The specification pattern provides a LINQ-like query interface that is database-agnostic and supports:
- Filtering with complex expressions
- Multiple ordering (OrderBy, ThenBy)
- Pagination (Skip/Take)
- Grouping
- Projections (Select)
- Specification composition (AND/OR/NOT)

## Basic Usage Examples

### 1. Direct AsQueryable Access (Like EF Core)

```csharp
// Traditional EF-style querying
var users = await repository.AsQueryable()
    .Where(u => u.Name.Contains("John"))
    .OrderBy(u => u.CreatedDate)
    .Skip(10)
    .Take(20)
    .ToListAsync(); // This would be in the implementation layer
```

### 2. Fluent Query Specification

```csharp
// Using the fluent query specification
var userQuery = repository.Query()
    .Where(u => u.Name.Contains("John"))
    .OrderBy(u => u.CreatedDate)
    .Skip(10)
    .Take(20);

var users = await repository.Find(userQuery.ToSpecification());
```

### 3. Expression-Based Specifications

```csharp
// Create reusable specifications
var activeUsersSpec = new ExpressionSpecification<User>(u => u.IsActive);
var recentUsersSpec = new ExpressionSpecification<User>(u => u.CreatedDate > DateTime.Now.AddDays(-30));

// Use specifications
var activeUsers = await repository.Find(activeUsersSpec);
var recentUsers = await repository.Find(recentUsersSpec);
```

## Advanced Usage Examples

### 4. Specification Composition

```csharp
// Combine specifications with AND/OR logic
var activeRecentUsersSpec = activeUsersSpec.And(recentUsersSpec);
var activeOrRecentUsersSpec = activeUsersSpec.Or(recentUsersSpec);
var inactiveUsersSpec = activeUsersSpec.Not();

// Use composed specifications
var activeRecentUsers = await repository.Find(activeRecentUsersSpec);
var activeOrRecentUsers = await repository.Find(activeOrRecentUsersSpec);
var inactiveUsers = await repository.Find(inactiveUsersSpec);
```

### 5. Complex Query Building

```csharp
// Build complex queries step by step
var complexQuery = repository.Query()
    .Where(u => u.Email.EndsWith("@company.com"))
    .Where(u => u.IsActive)
    .Where(u => u.Roles.Any(r => r.Name == "Admin"))
    .OrderBy(u => u.Department)
    .ThenByDescending(u => u.CreatedDate)
    .Skip(0)
    .Take(50);

var adminUsers = await repository.Find(complexQuery.ToSpecification());
```

### 6. Projections (Select Operations)

```csharp
// Project to anonymous types or DTOs
var userSummaryProjection = repository.Query()
    .Where(u => u.IsActive)
    .Select(u => new UserSummaryDto 
    { 
        Id = u.Id, 
        Name = u.Name, 
        Email = u.Email 
    })
    .OrderBy(s => s.Name)
    .Take(100);

var userSummaries = await repository.FindProjected(userSummaryProjection);
```

### 7. Pagination with Total Count

```csharp
// Get paginated results with total count
var searchSpec = new ExpressionSpecification<User>(u => u.Name.Contains("John"));
var pagedResult = await repository.FindPaged(searchSpec, page: 1, pageSize: 20);

Console.WriteLine($"Found {pagedResult.TotalCount} users");
Console.WriteLine($"Page {pagedResult.Page} of {pagedResult.TotalPages}");
Console.WriteLine($"Has next page: {pagedResult.HasNextPage}");

foreach (var user in pagedResult.Items)
{
    Console.WriteLine($"- {user.Name} ({user.Email})");
}
```

### 8. Aggregation Operations

```csharp
// Count, Any, All operations
var activeUsersCount = await repository.Count(activeUsersSpec);
var hasActiveUsers = await repository.Any(activeUsersSpec);
var allUsersActive = await repository.All(activeUsersSpec);

// Min/Max operations
var oldestUserCreatedDate = await repository.Min(activeUsersSpec, u => u.CreatedDate);
var newestUserCreatedDate = await repository.Max(activeUsersSpec, u => u.CreatedDate);

// Sum/Average operations (for numeric properties)
var totalLoginCount = await repository.Sum(activeUsersSpec, u => u.LoginCount);
var averageLoginCount = await repository.Average(activeUsersSpec, u => u.LoginCount);
```

### 9. Common Specifications

```csharp
// Use pre-built common specifications
var userById = await repository.FindFirst(CommonSpecifications.ById<User>(userId));
var usersByIds = await repository.Find(CommonSpecifications.ByIds<User>(userIds));
var allUsers = await repository.Find(CommonSpecifications.All<User>());
```

### 10. Extension Methods for Convenience

```csharp
// Use convenience extension methods
var userExists = await repository.Exists(activeUsersSpec);
var firstActiveUser = await repository.GetFirst(activeUsersSpec); // Throws if not found
var singleAdminUser = await repository.GetSingle(adminUsersSpec); // Throws if not found or multiple
```

## Real-World Example: User Management System

```csharp
public class UserService
{
    private readonly IRepository<User> _userRepository;

    public UserService(IRepository<User> userRepository)
    {
        _userRepository = userRepository;
    }

    // Search users with filters and pagination
    public async Task<PagedResult<UserDto>> SearchUsers(
        string? nameFilter = null,
        string? emailFilter = null,
        bool? isActive = null,
        string? department = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Start with a query that gets all users
        var query = _userRepository.Query();

        // Apply filters conditionally
        if (!string.IsNullOrEmpty(nameFilter))
            query = query.Where(u => u.Name.Contains(nameFilter));

        if (!string.IsNullOrEmpty(emailFilter))
            query = query.Where(u => u.Email.Contains(emailFilter));

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        if (!string.IsNullOrEmpty(department))
            query = query.Where(u => u.Department == department);

        // Add ordering and pagination
        query = query
            .OrderBy(u => u.Department)
            .ThenBy(u => u.Name)
            .Paginate(page, pageSize);

        // Project to DTO
        var projection = query.Select(u => new UserDto
        {
            Id = u.Id,
            Name = u.Name,
            Email = u.Email,
            Department = u.Department,
            IsActive = u.IsActive,
            CreatedDate = u.CreatedDate
        });

        // Execute the query
        var users = await _userRepository.FindProjected(projection, cancellationToken);
        var totalCount = await _userRepository.Count(query.ToSpecification(), cancellationToken);

        return new PagedResult<UserDto>
        {
            Items = users,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    // Get users by role with caching-friendly specifications
    public async Task<IEnumerable<User>> GetUsersByRole(string roleName, CancellationToken cancellationToken = default)
    {
        var roleSpec = new ExpressionSpecification<User>(u => u.Roles.Any(r => r.Name == roleName));
        var activeSpec = new ExpressionSpecification<User>(u => u.IsActive);
        var combinedSpec = roleSpec.And(activeSpec);

        return await _userRepository.Find(combinedSpec, cancellationToken);
    }

    // Complex business logic with multiple specifications
    public async Task<IEnumerable<User>> GetUsersRequiringAttention(CancellationToken cancellationToken = default)
    {
        // Users who haven't logged in for 30 days
        var inactiveSpec = new ExpressionSpecification<User>(u => u.LastLoginDate < DateTime.Now.AddDays(-30));
        
        // Users with failed login attempts
        var failedLoginSpec = new ExpressionSpecification<User>(u => u.FailedLoginAttempts > 5);
        
        // Users with expired passwords
        var expiredPasswordSpec = new ExpressionSpecification<User>(u => u.PasswordExpiryDate < DateTime.Now);
        
        // Combine with OR logic (any of these conditions)
        var attentionSpec = inactiveSpec.Or(failedLoginSpec).Or(expiredPasswordSpec);
        
        // But only active users
        var activeSpec = new ExpressionSpecification<User>(u => u.IsActive);
        var finalSpec = attentionSpec.And(activeSpec);

        return await _userRepository.Find(finalSpec, cancellationToken);
    }
}
```

## Benefits

1. **Database Agnostic**: Works with any IQueryable provider (EF Core, MongoDB, etc.)
2. **Type Safe**: Full compile-time checking and IntelliSense support
3. **Composable**: Specifications can be combined and reused
4. **Testable**: Easy to unit test business logic with specifications
5. **Performance**: Expressions are compiled and executed efficiently
6. **Maintainable**: Complex queries can be broken down into reusable components
7. **LINQ Compatible**: Familiar syntax for developers already using LINQ

## Implementation Notes

- All async methods include CancellationToken support (following the project conventions)
- No "Async" suffix in method names (following the project conventions)
- Inline comments are used instead of XML documentation (following the project conventions)
- Specifications are immutable - each operation returns a new specification instance
- The pattern supports both immediate execution and deferred execution patterns
