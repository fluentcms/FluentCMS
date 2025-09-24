# Transaction Support in FluentCMS Repositories

The FluentCMS repository now supports both auto-save mode (default) and transactional mode. This allows developers to choose the appropriate mode based on their needs.

## Auto-Save Mode (Default Behavior)

By default, all repository operations automatically save changes to the database:

```csharp
// Auto-save mode - each operation saves immediately
var userRepository = serviceProvider.GetService<IRepository<User>>();

var user = new User { Name = "John Doe", Email = "john@example.com" };
await userRepository.Add(user); // Automatically saves

var updatedUser = await userRepository.GetById(user.Id);
updatedUser.Name = "Jane Doe";
await userRepository.Update(updatedUser); // Automatically saves
```

## Transactional Mode

For scenarios where you need to group multiple operations into a single transaction, you can use transactional mode:

```csharp
// Transactional mode - operations are batched
var userRepository = serviceProvider.GetService<ITransactionalRepository<User>>();
var roleRepository = serviceProvider.GetService<ITransactionalRepository<Role>>();

// Begin transaction
await userRepository.BeginTransaction();

try
{
    // These operations won't be saved until Commit is called
    var user = new User { Name = "John Doe", Email = "john@example.com" };
    await userRepository.Add(user);
    
    var role = new Role { Name = "Admin" };
    await roleRepository.Add(role);
    
    // Commit all changes
    await userRepository.Commit();
}
catch (Exception)
{
    // Rollback all changes
    await userRepository.Rollback();
    throw;
}
```

## Mixed Mode Usage

You can also use both modes in the same application:

```csharp
// Auto-save mode for simple operations
var userRepository = serviceProvider.GetService<IRepository<User>>();
var user = new User { Name = "John Doe", Email = "john@example.com" };
await userRepository.Add(user); // Automatically saved

// Transactional mode for complex operations
var transactionalUserRepository = serviceProvider.GetService<ITransactionalRepository<User>>();
var roleRepository = serviceProvider.GetService<ITransactionalRepository<Role>>();

await transactionalUserRepository.BeginTransaction();
try
{
    // Multiple related operations
    var updatedUser = await userRepository.GetById(user.Id);
    updatedUser.Name = "Jane Doe";
    await userRepository.Update(updatedUser);
    
    var role = new Role { Name = "User" };
    await roleRepository.Add(role);
    updatedUser.RoleId = role.Id;
    await userRepository.Update(updatedUser);
    
    await userRepository.Commit();
}
catch (Exception)
{
    await userRepository.Rollback();
    throw;
}
```

## Key Benefits

1. **Backward Compatible**: Existing code continues to work unchanged
2. **Flexible**: Choose the mode that fits your use case
3. **Safe**: Automatic rollback on exceptions
4. **Easy to Use**: Simple API for transaction management
5. **Event Support**: Repository events work in both modes

## Best Practices

1. Always use try/catch blocks when using transactions
2. Call `Commit()` to save changes or `Rollback()` to discard them
3. Check `IsTransactionActive` property to determine current mode
4. Use auto-save mode for simple, independent operations
5. Use transactional mode for complex operations that need to succeed or fail together
