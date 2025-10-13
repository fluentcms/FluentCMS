# FluentCMS.Providers.Repositories.EntityFramework

Entity Framework implementation of the provider repository for the FluentCMS provider system. This package provides database persistence for provider configurations using Entity Framework Core.

## Overview

This package implements the `IProviderRepository` interface using Entity Framework Core, enabling database-backed storage and management of provider configurations. It includes comprehensive data integrity constraints, transaction management, and performance optimizations.

## Key Features

- **Database Persistence**: Store provider configurations in SQL databases
- **Data Integrity**: Unique constraints and validation rules
- **Transaction Management**: ACID compliance with automatic retry policies
- **Performance Optimized**: Efficient queries with proper indexing
- **Audit Support**: Built-in audit fields for tracking changes
- **Migration Support**: Entity Framework migrations for schema management

## Components

### ProviderDbContext
The Entity Framework DbContext for provider data.

```csharp
public class ProviderDbContext : DbContext
{
    public DbSet<Provider> Providers { get; set; }
    
    // Automatic audit field management
    // Database constraints and indexes
    // Error handling with meaningful messages
}
```

**Features:**
- Automatic audit field updates (CreatedAt, UpdatedAt)
- Database constraints for data integrity
- Optimized indexes for performance
- Meaningful error messages for constraint violations

### ProviderRepository
Entity Framework implementation of `IProviderRepository`.

```csharp
public class ProviderRepository : IProviderRepository, IDisposable
{
    Task AddMany(IEnumerable<Provider> providers, CancellationToken cancellationToken = default);
    Task Update(Provider provider, CancellationToken cancellationToken = default);
    Task Remove(Provider provider, CancellationToken cancellationToken = default);
    Task<IEnumerable<Provider>> GetAll(CancellationToken cancellationToken = default);
    Task<Provider?> GetByAreaAndName(string area, string name, CancellationToken cancellationToken = default);
    Task<IEnumerable<Provider>> GetByArea(string area, CancellationToken cancellationToken = default);
}
```

**Features:**
- Transaction management with retry policies
- Comprehensive validation
- Performance optimizations (AsNoTracking for reads)
- Proper error handling and logging
- Resource cleanup with IDisposable

### Provider Entity
Enhanced provider entity with audit support.

```csharp
public class Provider : AuditableEntity
{
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Area { get; set; }
    public string ModuleType { get; set; }
    public bool IsActive { get; set; }
    public string? Options { get; set; }
    
    // Inherited from AuditableEntity
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
```

## Setup and Configuration

### 1. Package Installation

```bash
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
# or your preferred EF Core provider
```

### 2. Service Registration

```csharp
// Program.cs
services.AddDbContext<ProviderDbContext>(options =>
    options.UseSqlServer(connectionString));

services.AddProviders()
        .UseEntityFramework();
```

### 3. Database Migration

```bash
# Add migration
dotnet ef migrations add InitialCreate --context ProviderDbContext

# Update database
dotnet ef database update --context ProviderDbContext
```

### 4. Connection String Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=FluentCMS;Trusted_Connection=true;MultipleActiveResultSets=true"
  }
}
```

## Database Schema

### Providers Table

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | uniqueidentifier | PK | Primary key |
| Name | nvarchar(100) | NOT NULL | Provider name |
| DisplayName | nvarchar(200) | NOT NULL | Display name |
| Area | nvarchar(50) | NOT NULL | Functional area |
| ModuleType | nvarchar(200) | NOT NULL | Module type name |
| IsActive | bit | NOT NULL, DEFAULT(0) | Active status |
| Options | nvarchar(4000) | NULL | JSON options |
| CreatedAt | datetime2 | NOT NULL, DEFAULT(GETUTCDATE()) | Creation timestamp |
| UpdatedAt | datetime2 | NOT NULL, DEFAULT(GETUTCDATE()) | Update timestamp |
| CreatedBy | nvarchar(255) | NULL | Created by user |
| UpdatedBy | nvarchar(255) | NULL | Updated by user |

### Indexes

```sql
-- Performance indexes
CREATE INDEX IX_Provider_Area ON Providers (Area);
CREATE INDEX IX_Provider_ModuleType ON Providers (ModuleType);
CREATE INDEX IX_Provider_Area_IsActive ON Providers (Area, IsActive) WHERE IsActive = 1;

-- Unique constraints
CREATE UNIQUE INDEX UX_Provider_Area_Name ON Providers (Area, Name);
```

### Check Constraints

```sql
-- Ensure only one active provider per area (SQL Server)
ALTER TABLE Providers ADD CONSTRAINT CK_Provider_SingleActivePerArea 
CHECK (IsActive = 0 OR NOT EXISTS (
    SELECT 1 FROM Providers p2 
    WHERE p2.Area = Area AND p2.IsActive = 1 AND p2.Id != Id
));
```

## Usage Examples

### Basic Operations

```csharp
public class ProviderManagementService
{
    private readonly IProviderRepository _repository;
    private readonly ILogger<ProviderManagementService> _logger;

    public ProviderManagementService(
        IProviderRepository repository,
        ILogger<ProviderManagementService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Provider> CreateProviderAsync(string area, string name, string moduleType, object? options = null)
    {
        var provider = new Provider
        {
            Area = area,
            Name = name,
            DisplayName = name,
            ModuleType = moduleType,
            IsActive = false,
            Options = options != null ? JsonSerializer.Serialize(options) : null
        };

        await _repository.AddMany([provider]);
        _logger.LogInformation("Created provider {Name} in area {Area}", name, area);
        
        return provider;
    }

    public async Task ActivateProviderAsync(string area, string name)
    {
        // Deactivate all providers in the area
        var existingProviders = await _repository.GetByArea(area);
        foreach (var provider in existingProviders.Where(p => p.IsActive))
        {
            provider.IsActive = false;
            await _repository.Update(provider);
        }

        // Activate the specified provider
        var targetProvider = await _repository.GetByAreaAndName(area, name);
        if (targetProvider == null)
            throw new InvalidOperationException($"Provider {name} not found in area {area}");

        targetProvider.IsActive = true;
        await _repository.Update(targetProvider);
        
        _logger.LogInformation("Activated provider {Name} in area {Area}", name, area);
    }
}
```

### Advanced Queries

```csharp
public class ProviderAnalyticsService
{
    private readonly ProviderDbContext _context;

    public ProviderAnalyticsService(ProviderDbContext context)
    {
        _context = context;
    }

    public async Task<Dictionary<string, int>> GetProviderCountByAreaAsync()
    {
        return await _context.Providers
            .GroupBy(p => p.Area)
            .Select(g => new { Area = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Area, x => x.Count);
    }

    public async Task<IEnumerable<Provider>> GetRecentlyModifiedProvidersAsync(int days = 7)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-days);
        return await _context.Providers
            .Where(p => p.UpdatedAt >= cutoffDate)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<string>> GetAvailableAreasAsync()
    {
        return await _context.Providers
            .Select(p => p.Area)
            .Distinct()
            .OrderBy(area => area)
            .ToListAsync();
    }
}
```

### Batch Operations

```csharp
public class ProviderBatchService
{
    private readonly IProviderRepository _repository;

    public ProviderBatchService(IProviderRepository repository)
    {
        _repository = repository;
    }

    public async Task ImportProvidersAsync(IEnumerable<ProviderImportModel> imports)
    {
        var providers = imports.Select(import => new Provider
        {
            Area = import.Area,
            Name = import.Name,
            DisplayName = import.DisplayName,
            ModuleType = import.ModuleType,
            IsActive = import.IsActive,
            Options = import.Options
        });

        await _repository.AddMany(providers);
    }

    public async Task BulkUpdateProviderStatusAsync(string area, bool isActive)
    {
        var providers = await _repository.GetByArea(area);
        
        foreach (var provider in providers)
        {
            provider.IsActive = isActive;
            await _repository.Update(provider);
        }
    }
}
```

## Advanced Configuration

### Custom DbContext Configuration

```csharp
public class CustomProviderDbContext : ProviderDbContext
{
    public CustomProviderDbContext(DbContextOptions<CustomProviderDbContext> options) 
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Custom configurations
        modelBuilder.Entity<Provider>(entity =>
        {
            // Custom table name
            entity.ToTable("CmsProviders");

            // Additional indexes
            entity.HasIndex(p => p.CreatedAt)
                  .HasDatabaseName("IX_Provider_CreatedAt");

            // Custom check constraints for your database
            if (Database.IsNpgsql()) // PostgreSQL
            {
                entity.ToTable(t => t.HasCheckConstraint(
                    "CK_Provider_SingleActivePerArea_PG",
                    "IsActive = false OR NOT EXISTS (SELECT 1 FROM \"CmsProviders\" p2 WHERE p2.\"Area\" = \"Area\" AND p2.\"IsActive\" = true AND p2.\"Id\" != \"Id\")"));
            }
        });
    }
}
```

### Multi-Tenant Support

```csharp
public class TenantProviderDbContext : ProviderDbContext
{
    private readonly ITenantProvider _tenantProvider;

    public TenantProviderDbContext(
        DbContextOptions<TenantProviderDbContext> options,
        ITenantProvider tenantProvider) 
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Provider>(entity =>
        {
            // Add tenant ID column
            entity.Property<string>("TenantId")
                  .HasMaxLength(50)
                  .IsRequired();

            // Add tenant to unique constraint
            entity.HasIndex(p => new { p.Area, p.Name, EF.Property<string>(p, "TenantId") })
                  .IsUnique()
                  .HasDatabaseName("UX_Provider_Area_Name_Tenant");

            // Global query filter for tenant isolation
            entity.HasQueryFilter(p => EF.Property<string>(p, "TenantId") == _tenantProvider.TenantId);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Automatically set tenant ID
        var entries = ChangeTracker.Entries<Provider>()
            .Where(e => e.State == EntityState.Added);

        foreach (var entry in entries)
        {
            entry.Property("TenantId").CurrentValue = _tenantProvider.TenantId;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
```

### Performance Optimization

```csharp
// Read-only repository for performance-critical scenarios
public class ReadOnlyProviderRepository
{
    private readonly ProviderDbContext _context;

    public ReadOnlyProviderRepository(ProviderDbContext context)
    {
        _context = context;
    }

    public async Task<Provider?> GetActiveProviderAsync(string area)
    {
        return await _context.Providers
            .AsNoTracking()
            .Where(p => p.Area == area && p.IsActive)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<Provider>> GetProvidersByAreasAsync(IEnumerable<string> areas)
    {
        return await _context.Providers
            .AsNoTracking()
            .Where(p => areas.Contains(p.Area))
            .OrderBy(p => p.Area)
            .ThenBy(p => p.Name)
            .ToListAsync();
    }
}
```

## Error Handling

### Common Database Errors

```csharp
public class ProviderRepositoryExtensions
{
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        ILogger logger,
        int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning("Concurrency conflict on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                
                if (attempt == maxRetries)
                    throw new InvalidOperationException("Operation failed due to concurrent modifications", ex);
                    
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE") == true)
            {
                throw new InvalidOperationException("A provider with the same name already exists in this area", ex);
            }
        }

        throw new InvalidOperationException("Operation failed after maximum retries");
    }
}
```

## Migration Scripts

### Initial Migration

```csharp
// Add-Migration InitialCreate
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Providers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Area = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ModuleType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                Options = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                CreatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                UpdatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Providers", x => x.Id);
                table.CheckConstraint("CK_Provider_SingleActivePerArea", "IsActive = 0 OR NOT EXISTS (SELECT 1 FROM Providers p2 WHERE p2.Area = Area AND p2.IsActive = 1 AND p2.Id != Id)");
            },
            comment: "Stores provider configurations for the FluentCMS provider system");

        migrationBuilder.CreateIndex(
            name: "IX_Provider_Area",
            table: "Providers",
            column: "Area");

        migrationBuilder.CreateIndex(
            name: "IX_Provider_Area_IsActive",
            table: "Providers",
            columns: new[] { "Area", "IsActive" },
            filter: "IsActive = 1");

        migrationBuilder.CreateIndex(
            name: "IX_Provider_ModuleType",
            table: "Providers",
            column: "ModuleType");

        migrationBuilder.CreateIndex(
            name: "UX_Provider_Area_Name",
            table: "Providers",
            columns: new[] { "Area", "Name" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Providers");
    }
}
```

## Best Practices

1. **Connection Management**: Use connection pooling and proper disposal
2. **Transactions**: Wrap related operations in transactions
3. **Indexing**: Create appropriate indexes for query patterns
4. **Validation**: Validate data before database operations
5. **Error Handling**: Implement proper exception handling and logging
6. **Performance**: Use `AsNoTracking()` for read-only operations
7. **Security**: Use parameterized queries (handled by EF Core)

## Dependencies

- Microsoft.EntityFrameworkCore (>= 9.0.0)
- Microsoft.EntityFrameworkCore.SqlServer (>= 9.0.0) [or your preferred provider]
- FluentCMS.Providers.Abstractions
- FluentCMS.Providers

## See Also

- [FluentCMS.Providers](../FluentCMS.Providers/README.md) - Core provider system
- [FluentCMS.Providers.Abstractions](../FluentCMS.Providers.Abstractions/README.md) - Provider abstractions
- [Entity Framework Core Documentation](https://docs.microsoft.com/en-us/ef/core/)
