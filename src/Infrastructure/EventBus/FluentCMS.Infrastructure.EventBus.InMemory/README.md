# FluentCMS.EventBus.InMemory

**In-memory event bus implementation for FluentCMS, built for .NET 9.**

## Overview

`FluentCMS.EventBus.InMemory` is a lightweight, high-performance event bus for .NET applications. It enables decoupled communication between components using the publish/subscribe pattern, making it ideal for development, testing, and single-instance production scenarios.

- **Publish/Subscribe** pattern for domain events
- **Dependency Injection**-friendly
- **Configurable error handling** (fail-fast or aggregate)
- **Automatic scope management** for event handlers
- **Detailed logging** for diagnostics

## Table of Contents
- [Getting Started](#getting-started)
  - [Installation](#installation)
- [Registration](#registration)
- [Usage](#usage)
  - [Define an Event](#define-an-event)
  - [Create a Subscriber](#create-a-subscriber)
  - [Register a Subscriber](#register-a-subscriber)
  - [Publish an Event](#publish-an-event)
- [Configuration](#configuration)
- [Logging](#logging)
- [Best Practices](#best-practices)
- [Extending](#extending)
- [Contributing](#contributing)
- [License](#license)

## Getting Started

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Reference to `FluentCMS.EventBus.Abstractions`

## Installation

Add the project or NuGet package to your solution:

```sh
dotnet add package FluentCMS.EventBus.InMemory
```

## Registration

Register the in-memory event bus in your DI container (typically in `Startup.cs` or your service configuration):

```csharp
using FluentCMS.EventBus.InMemory;

builder.Services.AddInMemoryEventBus(options =>
{
    options.Mode = EventPublisherOptions.ErrorHandlingMode.Aggregate; // or FailFast
});
```

## Usage

### Define an Event

```csharp
public class UserRegisteredEvent : IEvent
{
    public string UserId { get; set; }
}
```

### Create a Subscriber

```csharp
public class SendWelcomeEmailSubscriber : IEventSubscriber<UserRegisteredEvent>
{
    public Task Handle(UserRegisteredEvent @event, CancellationToken cancellationToken)
    {
        // Example: Send a welcome email to the user
        Console.WriteLine($"Welcome email sent to user {@event.UserId}");
        return Task.CompletedTask;
    }
}
```

### Register a Subscriber

```csharp
services.AddScoped<IEventSubscriber<UserRegisteredEvent>, SendWelcomeEmailSubscriber>();
```

### Publish an Event

```csharp
public class UserService
{
    private readonly IEventPublisher _eventPublisher;

    public UserService(IEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;
    }

    public async Task RegisterUserAsync(string userId)
    {
        // Registration logic...
        await _eventPublisher.Publish(new UserRegisteredEvent { UserId = userId });
    }
}
```

## Configuration

You can configure error handling mode via `EventPublisherOptions`:

- `FailFast`: Stops on the first handler exception.
- `Aggregate` (default): Runs all handlers, aggregates exceptions, and throws at the end.

```csharp
services.AddInMemoryEventBus(options =>
{
    options.Mode = EventPublisherOptions.ErrorHandlingMode.FailFast;
});
```

## Error Handling & Execution Behavior

### FailFast Mode
- **Execution:** Handlers run **sequentially** in registration order
- **On Error:** Stops immediately when first handler throws
- **Use Case:** When you need guaranteed execution order and want to fail fast

```csharp
services.AddInMemoryEventBus(options =>
{
    options.Mode = EventPublisherOptions.ErrorHandlingMode.FailFast;
});
```

### Aggregate Mode (Default)
- **Execution:** Handlers run **concurrently** using Task.WhenAll
- **On Error:** All handlers complete, then throws aggregate exception
- **Use Case:** Best performance when handlers are independent

```csharp
services.AddInMemoryEventBus(options =>
{
    options.Mode = EventPublisherOptions.ErrorHandlingMode.Aggregate;
});
```

### Thread Safety Considerations
⚠️ **Important:** In Aggregate mode, handlers execute concurrently. Ensure your handlers:
- Don't share mutable state
- Are thread-safe if they access shared resources
- Don't depend on execution order

For ordered execution, use FailFast mode or implement coordination in handlers.

## Logging

- **Information**: Successful event publishing, including event type and subscriber count.
- **Warning**: No subscribers found for an event.
- **Error**: Handler exceptions, with detailed context.
- **Debug**: Scope management and context usage.

## Best Practices

- Use the in-memory event bus for development, testing, or single-instance production scenarios.
- For distributed or multi-instance deployments, consider a persistent or message-queue-based event bus.
- Register event subscribers with appropriate lifetimes (scoped, transient, or singleton as needed).
- Use meaningful event and subscriber names for clarity and maintainability.
- Handle exceptions in subscribers responsibly; avoid long-running or blocking operations in event handlers.

## Extending

You can extend or customize the event bus by implementing your own `IEventPublisher` or `IEventSubscriber<TEvent>`.

## Contributing

Contributions are welcome! Please open issues or submit pull requests for improvements or bug fixes.

## License

MIT License

---

**FluentCMS.EventBus.InMemory** is part of the [FluentCMS](https://github.com/fluentcms/FluentCMS) ecosystem.
