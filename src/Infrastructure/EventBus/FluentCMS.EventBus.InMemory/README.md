# FluentCMS.EventBus.InMemory

**In-memory event bus implementation for FluentCMS, built for .NET 9.**

## Overview

`FluentCMS.EventBus.InMemory` provides a simple, high-performance, in-memory event bus for .NET applications. It is designed for scenarios where low latency and simplicity are required, such as development, testing, or single-instance deployments.

- **Publish/Subscribe** pattern for domain events
- **Dependency Injection**-friendly
- **Configurable error handling** (fail-fast or aggregate)
- **Automatic scope management** for event handlers
- **Detailed logging** for diagnostics

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Reference to `FluentCMS.EventBus.Abstractions`

### Installation

Add the project or NuGet package to your solution:

```sh
dotnet add package FluentCMS.EventBus.InMemory
```

### Registration

Register the in-memory event bus in your DI container (typically in `Startup.cs` or your service configuration):

```csharp
using FluentCMS.EventBus.InMemory;

builder.Services.AddInMemoryEventBus(options =>
{
    options.Mode = EventPublisherOptions.ErrorHandlingMode.Aggregate; // or FailFast
});
```

### Usage

#### Define an Event

```csharp
public class UserRegisteredEvent : IEvent
{
    public string UserId { get; set; }
}
```

#### Create a Subscriber

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

Register your subscriber with DI:

```csharp
services.AddScoped<IEventSubscriber<UserRegisteredEvent>, SendWelcomeEmailSubscriber>();
```

#### Publish an Event

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

## Logging

- **Information**: Successful event publishing, including event type and subscriber count.
- **Warning**: No subscribers found for an event.
- **Error**: Handler exceptions, with detailed context.
- **Debug**: Scope management and context usage.

## Best Practices

- Use the in-memory event bus for development, testing, or single-instance production scenarios.
- For distributed or multi-instance deployments, consider a persistent or message-queue-based event bus.
- Register event subscribers with appropriate lifetimes (scoped, transient, or singleton as needed).

## Extending

You can extend or customize the event bus by implementing your own `IEventPublisher` or `IEventSubscriber<TEvent>`.

## Contributing

Contributions are welcome! Please open issues or submit pull requests for improvements or bug fixes.

## License

MIT License

---

**FluentCMS.EventBus.InMemory** is part of the [FluentCMS](https://github.com/fluentcms/FluentCMS) ecosystem.
