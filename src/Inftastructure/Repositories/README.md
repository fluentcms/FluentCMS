# FluentCMS.Repositories

A comprehensive repository pattern implementation for .NET applications with Entity Framework Core, providing transaction support, event publishing, and audit trails.

## Features

- **Generic Repository Pattern** - Clean, reusable data access layer
- **Transaction Support** - Both auto-save and manual transaction modes
- **Event Publishing** - Automatic repository events for entity operations
- **Audit Trail** - Automatic tracking of created/updated timestamps and users
- **Multiple Database Support** - SQLite and SQL Server out of the box
- **Interceptors** - Extensible architecture with built-in interceptors
- **Async/Await** - Full asynchronous support throughout

## Installation

```csharp
// In Program.cs or Startup.cs
services.AddSqliteDatabase("Data Source=app.db"); // or AddSqlServerDatabase("connectionString")
services.AddGenericRepository<User, AppDbContext>();
```

## Quick Start

### Basic Usage (Auto-Save Mode)

```csharp
// Auto-save mode - each operation saves immediately
var userRepository = serviceProvider.GetService<IRepository<User>>();

var user = new User { Name = "John Doe", Email = "john@example.com" };
await userRepository.Add(user); // Automatically saves

var users = await userRepository.GetAll();
var userById = await userRepository.GetById(userId);
```

### Transactional Usage

```csharp
// Transactional mode for grouping operations
var userRepository = serviceProvider.GetService<ITransactionalRepository<User>>();
var roleRepository = serviceProvider.GetService<ITransactionalRepository<Role>>();

await userRepository.BeginTransaction();
try
{
    var user = new User { Name = "John Doe", Email = "john@example.com" };
    await userRepository.Add(user);
    
    var role = new Role { Name = "Admin" };
    await roleRepository.Add(role);
    
    await userRepository.Commit(); // Save all changes
}
catch (Exception)
{
    await userRepository.Rollback(); // Discard all changes
    throw;
}
```

## Architecture

### Abstractions Layer

The `FluentCMS.Repositories.Abstractions` project contains interface definitions:

- `IRepository<TEntity>` - Core repository operations
- `ITransactionalRepository<TEntity>` - Transaction support
- `IRepositoryEventPublisher` - Event publishing interface
- `IDatabaseConfiguration` - Database configuration abstraction

### Entity Framework Implementation

The `FluentCMS.Repositories.EntityFramework` project provides Entity Framework Core implementation:

- `Repository<TEntity, TContext>` - Generic repository implementation
- `RepositoryEventPublisher` - Event publisher implementation
- Database configuration classes and registration extensions

## Transaction Support

### Auto-Save Mode (Default)

All operations automatically save to the database:

```csharp
var userRepository = serviceProvider.GetService<IRepository<User>>();
var user = new User { Name = "John" };
await userRepository.Add(user); // Immediately saved
```

### Transactional Mode

Group multiple operations into a single transaction:

```csharp
var userRepository = serviceProvider.GetService<ITransactionalRepository<User>>();

await userRepository.BeginTransaction();
try
{
    await userRepository.Add(user1);
    await userRepository.Add(user2);
    await userRepository.Commit(); // All saved together
}
catch
{
    await userRepository.Rollback(); // All discarded
    throw;
}
```

See [TransactionUsage.md](FluentCMS.Repositories.EntityFramework/TransactionUsage.md) for detailed examples.

## Event System

Automatic repository events are published for entity operations:

```csharp
// Events are automatically published for:
// - RepositoryEntityCreatedEvent (on Add)
// - RepositoryEntityUpdatedEvent (on Update)  
// - RepositoryEntityRemovedEvent (on Remove)
```

Events are handled by the `RepositoryEventBusPublisherInterceptor` and can be consumed by implementing `IEventPublisher`.

## Audit Trail

Entities implementing `IAuditableEntity` automatically get audit properties:

- `CreatedBy` / `CreatedAt` - Set on creation
- `UpdatedBy` / `UpdatedAt` - Set on updates
- `Version` - Incremented on each update

Handled by the `AuditableEntityInterceptor`.

## Database Configuration

### SQLite

```csharp
services.AddSqliteDatabase("Data Source=app.db");
```

### SQL Server

```csharp
services.AddSqlServerDatabase("Server=.;Database=AppDb;Integrated Security=true;");
```

### Custom Databases

Implement `IDatabaseConfiguration` for other database providers.

## API Reference

### IRepository<TEntity>

- `Add(TEntity entity)` - Add entity
- `AddMany(IEnumerable<TEntity> entities)` - Add multiple entities
- `Update(TEntity entity)` - Update entity
- `Remove(TEntity entity)` - Remove entity
- `Remove(Guid id)` - Remove entity by ID
- `GetById(Guid id)` - Get entity by ID
- `GetAll()` - Get all entities
- `Find(Expression<Func<TEntity, bool>> predicate)` - Find entities by predicate
- `Count(Expression<Func<TEntity, bool>> filter)` - Count entities
- `Any(Expression<Func<TEntity, bool>> filter)` - Check if entities exist

### ITransactionalRepository<TEntity>

- `BeginTransaction()` - Start transaction
- `Commit()` - Commit transaction
- `Rollback()` - Rollback transaction
- `IsTransactionActive` - Check transaction status

## Interceptors

- `AuditableEntityInterceptor` - Handles audit trail properties
- `RepositoryEventBusPublisherInterceptor` - Publishes repository events

## Best Practices

1. Use auto-save mode for simple, independent operations
2. Use transactional mode for complex operations that need to succeed/fail together
3. Always wrap transactional operations in try/catch blocks
4. Check `IsTransactionActive` to determine current mode
5. Implement proper error handling for repository operations

## Example Entity

```csharp
public class User : IEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    
    // Audit properties (automatically managed)
    public string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Version { get; set; }
}
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## License

MIT
