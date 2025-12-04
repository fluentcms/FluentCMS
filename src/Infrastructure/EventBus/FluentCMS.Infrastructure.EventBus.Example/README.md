# FluentCMS EventBus - Example Application

This example application demonstrates how to use the FluentCMS EventBus system in a real-world scenario.

## Features Demonstrated

### 1. Event Publishing
- **UserRegisteredEvent**: Demonstrates handling user registration with multiple handlers
- **OrderPlacedEvent**: Shows order processing with concurrent handlers

### 2. Multiple Event Handlers
Each event can have multiple handlers that execute independently:
- **User Registration**:
  - `SendWelcomeEmailHandler` - Sends welcome email to new users
  - `CreateUserProfileHandler` - Creates user profile in database
  - `LogUserActivityHandler` - Logs user activity for audit

- **Order Processing**:
  - `UpdateInventoryHandler` - Updates inventory levels
  - `SendOrderConfirmationHandler` - Sends order confirmation email

### 3. Execution Modes

#### Aggregate Mode (Default)
- Handlers execute **concurrently** for better performance
- All handlers complete even if some fail
- Exceptions are collected and thrown as `EventPublisherAggregatedException`

#### FailFast Mode
- Handlers execute **sequentially** in registration order
- Execution stops immediately on first error
- Useful when handler order matters or for debugging

## Running the Example

```bash
cd FluentCMS.Infrastructure.EventBus.Example
dotnet run
```

## Code Examples

### Configuring Aggregate Mode (Default)
```csharp
services.AddInMemoryEventBus(options =>
{
    options.Mode = EventPublisherOptions.ErrorHandlingMode.Aggregate;
});
```

### Configuring FailFast Mode
```csharp
services.AddInMemoryEventBus(options =>
{
    options.Mode = EventPublisherOptions.ErrorHandlingMode.FailFast;
});
```

### Registering Event Handlers
```csharp
services.AddEventHandler<UserRegisteredEvent, SendWelcomeEmailHandler>();
services.AddEventHandler<UserRegisteredEvent, CreateUserProfileHandler>();
services.AddEventHandler<UserRegisteredEvent, LogUserActivityHandler>();
```

### Publishing Events
```csharp
var userEvent = new UserRegisteredEvent
{
    UserId = "USER-001",
    Email = "john.doe@example.com",
    FullName = "John Doe"
};

await publisher.Publish(userEvent);
```

## Key Concepts

1. **Events inherit from EventBase** which provides:
   - `EventId` - Unique identifier for each event instance
   - `OccurredAt` - Timestamp when event was created

2. **Handlers implement IEventSubscriber<TEvent>**:
   - Scoped lifetime by default
   - Support async operations
   - Respect cancellation tokens

3. **Automatic Dependency Injection**:
   - Handlers can inject services via constructor
   - Examples use `ILogger<T>` for demonstration

4. **Error Handling**:
   - Aggregate mode: Continue executing all handlers, collect errors
   - FailFast mode: Stop on first error for immediate feedback

## Performance Comparison

Based on the example output, you'll see:
- **Concurrent execution** (Aggregate mode): ~120ms for 3 handlers
- **Sequential execution** (FailFast mode): ~180ms for 3 handlers

Concurrent execution is faster because handlers run in parallel!
