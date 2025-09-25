# FluentCMS.Repositories.Abstractions

A powerful and flexible repository abstraction layer for FluentCMS, implementing the Repository and Specification patterns with a fluent API for building complex queries.

## 🚀 Quick Start

### Basic Setup

```csharp
// Inject your repository implementation
public class ProductService
{
    private readonly IRepository<Product> _productRepository;
    
    public ProductService(IRepository<Product> productRepository)
    {
        _productRepository = productRepository;
    }
}
```

### Simple CRUD Operations

```csharp
// Create
var product = new Product { Name = "Laptop", Price = 999.99m };
var createdProduct = await _productRepository.Add(product);

// Read
var product = await _productRepository.GetById(productId);
var allProducts = await _productRepository.GetAll();

// Update
product.Price = 899.99m;
var updatedProduct = await _productRepository.Update(product);

// Delete
var deletedProduct = await _productRepository.Remove(productId);
```

### NEW: Fluent Query Operations ✨

```csharp
// Direct fluent queries - no double repository calls!
var expensiveProducts = await _productRepository
    .Query()
    .Where(p => p.Price > 500)
    .OrderByDescending(p => p.Price)
    .Query();

// Complex queries with pagination
var pagedProducts = await _productRepository
    .Query()
    .Where(p => p.IsActive)
    .Where(p => p.Category.Name == "Electronics")
    .OrderBy(p => p.Name)
    .FindPaged(page: 1, pageSize: 20);

// Count and aggregations
var activeCount = await _productRepository
    .Query()
    .Where(p => p.IsActive)
    .Count();

var totalValue = await _productRepository
    .Query()
    .Where(p => p.IsActive)
    .Sum(p => p.Price);
```

## 📋 Core Concepts

### Repository Pattern
Provides a consistent interface for data access operations, abstracting away the underlying data storage implementation.

### Specification Pattern
Encapsulates query logic in reusable, composable objects that can be combined and tested independently.

### Fluent Query API
Offers LINQ-like syntax for building complex queries in a readable and maintainable way.

## 🔧 API Reference

### IRepository<TEntity>

#### Basic Operations
- `Task<TEntity> Add(TEntity entity, CancellationToken cancellationToken = default)`
- `Task<IEnumerable<TEntity>> AddRange(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)`
- `Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default)`
- `Task<TEntity?> Remove(Guid id, CancellationToken cancellationToken = default)`
- `Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)`
- `Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)`

#### Specification-based Operations
- `Task<IEnumerable<TEntity>> Query(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)`
- `Task<TEntity?> FirstOrDefault(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)`
- `Task<TEntity?> SingleOrDefault(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)`
- `Task<long> Count(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)`
- `Task<bool> Any(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)`
- `Task<PagedResult<TEntity>> FindPaged(ISpecification<TEntity> specification, int page, int pageSize, CancellationToken cancellationToken = default)`

#### Aggregation
- `Task<decimal> Sum(ISpecification<TEntity> specification, Expression<Func<TEntity, decimal>> selector, CancellationToken cancellationToken = default)`

### ISpecification<T>
- `IQueryable<T> Apply(IQueryable<T> query)` - Applies the specification logic to a queryable

### IQuerySpecification<T>
Fluent interface for building queries:

#### Filtering
- `IQuerySpecification<T> Where(Expression<Func<T, bool>> predicate)`

#### Ordering
- `IQuerySpecification<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)`
- `IQuerySpecification<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)`
- `IQuerySpecification<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)`
- `IQuerySpecification<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)`

#### Pagination
- `IQuerySpecification<T> Skip(int count)`
- `IQuerySpecification<T> Take(int count)`

#### Other Operations
- `IQuerySpecification<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)`
- `IQuerySpecification<T> Distinct()`

#### Build & Convert
- `IQueryable<T> Build(IQueryable<T> source)`
- `ISpecification<T> ToSpecification()`

## 📚 Usage Examples

### Simple Specifications

```csharp
// Using expression-based specification
var activeProducts = await _productRepository.Query(
    SpecificationExtensions.Where<Product>(p => p.IsActive)
);

// Using NEW fluent syntax (recommended!) - single repository call
var expensiveProducts = await _productRepository
    .Query()
    .Where(p => p.Price > 500)
    .OrderByDescending(p => p.Price)
    .Query();

// Old approach still works for backward compatibility
var expensiveProductsOld = await _productRepository.Query(
    _productRepository.Query()
        .Where(p => p.Price > 500)
        .OrderByDescending(p => p.Price)
        .ToSpecification()
);
```

### Complex Queries with Fluent API

```csharp
// NEW: Direct execution with fluent API (recommended!)
var products = await _productRepository
    .Query()
    .Where(p => p.IsActive)
    .Where(p => p.Price >= 100 && p.Price <= 1000)
    .Where(p => p.Category.Name == "Electronics")
    .OrderBy(p => p.Name)
    .ThenByDescending(p => p.CreatedDate)
    .Query();

// Old approach: Build specification first (still supported)
var specification = _productRepository.Query()
    .Where(p => p.IsActive)
    .Where(p => p.Price >= 100 && p.Price <= 1000)
    .Where(p => p.Category.Name == "Electronics")
    .OrderBy(p => p.Name)
    .ThenByDescending(p => p.CreatedDate)
    .ToSpecification();

var productsOld = await _productRepository.Query(specification);
```

### Pagination

```csharp
// Using built-in pagination
var pagedResult = await _productRepository.FindPaged(
    SpecificationExtensions.Where<Product>(p => p.IsActive),
    page: 1,
    pageSize: 10
);

Console.WriteLine($"Page {pagedResult.Page} of {pagedResult.TotalPages}");
Console.WriteLine($"Total items: {pagedResult.TotalCount}");

// Using fluent pagination
var specification = _productRepository.Query()
    .Where(p => p.IsActive)
    .Paginate(page: 2, pageSize: 20)
    .ToSpecification();
```

### Dynamic Ordering

```csharp
// Safe property-based ordering with caching
var specification = _productRepository.Query()
    .Where(p => p.IsActive)
    .OrderByProperty("Name") // Cached reflection
    .ToSpecification();

// Type-safe alternative
var specification2 = _productRepository.Query()
    .Where(p => p.IsActive)
    .OrderByProperty(p => p.Name) // Compile-time safety
    .ToSpecification();
```

### Aggregations

```csharp
// Count with specification
var activeProductCount = await _productRepository.Count(
    SpecificationExtensions.Where<Product>(p => p.IsActive)
);

// Check existence
var hasExpensiveProducts = await _productRepository.Any(
    SpecificationExtensions.Where<Product>(p => p.Price > 1000)
);

// Sum with specification
var totalValue = await _productRepository.Sum(
    SpecificationExtensions.Where<Product>(p => p.IsActive),
    p => p.Price
);
```

### Custom Specifications

```csharp
// Create reusable specification classes
public class ActiveProductsSpecification : Specification<Product>
{
    public override IQueryable<Product> Apply(IQueryable<Product> query)
    {
        return query.Where(p => p.IsActive && !p.IsDeleted);
    }
}

public class ProductsByCategorySpecification : Specification<Product>
{
    private readonly string _categoryName;
    
    public ProductsByCategorySpecification(string categoryName)
    {
        _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
    }
    
    public override IQueryable<Product> Apply(IQueryable<Product> query)
    {
        return query.Where(p => p.Category.Name == _categoryName);
    }
}

// Usage
var activeProducts = await _productRepository.Query(new ActiveProductsSpecification());
var electronicsProducts = await _productRepository.Query(new ProductsByCategorySpecification("Electronics"));
```

## 🏆 Best Practices

### When to Use What

**Simple Queries**: Use `SpecificationExtensions.Where()` for straightforward filtering.
```csharp
var activeUsers = await _userRepository.Query(
    SpecificationExtensions.Where<User>(u => u.IsActive)
);
```

**Complex Queries**: Use fluent `Query()` API for multi-step operations.
```csharp
var specification = _userRepository.Query()
    .Where(u => u.IsActive)
    .Where(u => u.LastLoginDate >= DateTime.UtcNow.AddDays(-30))
    .OrderByDescending(u => u.LastLoginDate)
    .Take(100)
    .ToSpecification();
```

**Reusable Logic**: Create custom specification classes for business rules.
```csharp
public class RecentlyActiveUsersSpecification : Specification<User>
{
    private readonly int _days;
    
    public RecentlyActiveUsersSpecification(int days = 30)
    {
        _days = days;
    }
    
    public override IQueryable<User> Apply(IQueryable<User> query)
    {
        return query.Where(u => u.IsActive && 
                               u.LastLoginDate >= DateTime.UtcNow.AddDays(-_days));
    }
}
```

### Performance Tips

1. **Use Specification Caching**: The library automatically caches compiled expressions for property-based operations.

2. **Avoid N+1 Queries**: Use proper eager loading in your repository implementation.

3. **Prefer Typed Expressions**: Use strongly-typed property expressions over string-based ones when possible.

4. **Batch Operations**: Use `AddRange()` for multiple inserts.

5. **Pagination**: Always use pagination for large result sets to avoid memory issues.

### Error Handling

```csharp
try
{
    var products = await _productRepository.Query(specification);
}
catch (RepositoryException<Product> ex)
{
    // Handle repository-specific errors
    Console.WriteLine($"Repository error for {ex.EntityType}: {ex.Message}");
    if (ex.EntityId != null)
    {
        Console.WriteLine($"Entity ID: {ex.EntityId}");
    }
}
catch (InvalidOperationException ex)
{
    // Handle specification errors (e.g., ThenBy without OrderBy)
    Console.WriteLine($"Invalid operation: {ex.Message}");
}
catch (ArgumentException ex)
{
    // Handle invalid arguments (e.g., invalid property names)
    Console.WriteLine($"Invalid argument: {ex.Message}");
}
```

## 🛠️ Advanced Scenarios

### Combining Repository with Unit of Work

```csharp
public class ProductService
{
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<Category> _categoryRepository;
    
    public async Task<Product> CreateProductWithCategoryAsync(string productName, string categoryName)
    {
        // Check if category exists
        var category = await _categoryRepository.FirstOrDefault(
            SpecificationExtensions.Where<Category>(c => c.Name == categoryName)
        );
        
        if (category == null)
        {
            category = await _categoryRepository.Add(new Category { Name = categoryName });
        }
        
        var product = new Product 
        { 
            Name = productName, 
            CategoryId = category.Id 
        };
        
        return await _productRepository.Add(product);
    }
}
```

### Building Dynamic Filters

```csharp
public async Task<PagedResult<Product>> SearchProductsAsync(ProductSearchCriteria criteria)
{
    var query = _productRepository.Query();
    
    // Dynamic filtering based on criteria
    if (!string.IsNullOrEmpty(criteria.Name))
    {
        query = query.Where(p => p.Name.Contains(criteria.Name));
    }
    
    if (criteria.MinPrice.HasValue)
    {
        query = query.Where(p => p.Price >= criteria.MinPrice.Value);
    }
    
    if (criteria.MaxPrice.HasValue)
    {
        query = query.Where(p => p.Price <= criteria.MaxPrice.Value);
    }
    
    if (criteria.CategoryIds?.Any() == true)
    {
        query = query.Where(p => criteria.CategoryIds.Contains(p.CategoryId));
    }
    
    // Dynamic sorting
    if (!string.IsNullOrEmpty(criteria.SortBy))
    {
        query = query.OrderByProperty(criteria.SortBy, criteria.SortDescending);
    }
    else
    {
        query = query.OrderBy(p => p.Name);
    }
    
    // Pagination
    query = query.Paginate(criteria.Page, criteria.PageSize);
    
    return await query.FindPaged(criteria.Page, criteria.PageSize);
}
```

## 🐛 Troubleshooting

### Common Issues

**1. "ThenBy can only be used after OrderBy or OrderByDescending has been called"**
```csharp
// ❌ Wrong - ThenBy without OrderBy
var spec = _repository.Query()
    .Where(x => x.IsActive)
    .ThenBy(x => x.Name) // Error!
    .ToSpecification();

// ✅ Correct - OrderBy first
var spec = _repository.Query()
    .Where(x => x.IsActive)
    .OrderBy(x => x.CreatedDate)
    .ThenBy(x => x.Name) // OK!
    .ToSpecification();
```

**2. "Property 'XYZ' not found on type 'Product'"**
```csharp
// ❌ Wrong - typo in property name
var spec = _repository.Query()
    .OrderByProperty("Nmae") // Typo!
    .ToSpecification();

// ✅ Correct - use typed expression
var spec = _repository.Query()
    .OrderByProperty(p => p.Name) // Compile-time safety
    .ToSpecification();
```

**3. Performance Issues with Large Result Sets**
```csharp
// ❌ Wrong - loading all data
var allProducts = await _repository.GetAll(); // Avoid this!

// ✅ Correct - use pagination
var pagedProducts = await _repository.FindPaged(
    SpecificationExtensions.Where<Product>(p => p.IsActive),
    page: 1,
    pageSize: 50
);
```

### Debugging Tips

1. **Use ToSpecification() at the end**: Always call `ToSpecification()` on query specifications before passing to repository methods.

2. **Check parameter validation**: The library validates inputs and provides descriptive error messages.

3. **Monitor cache usage**: Property-based operations are cached automatically for better performance.

4. **Test specifications independently**: Specifications can be unit tested without database dependencies.

## 📄 License

This project is part of FluentCMS and follows the same licensing terms.

## 🤝 Contributing

Contributions are welcome! Please follow the existing code conventions and add appropriate tests for new features.

---

For more information about FluentCMS, visit the [main repository](https://github.com/fluentcms/FluentCMS).
