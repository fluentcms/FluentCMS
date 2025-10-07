# FluentCMS.EventBus.Abstractions

**Event bus abstractions for FluentCMS, targeting .NET 9.**

## Overview

`FluentCMS.EventBus.Abstractions` provides the core interfaces and base types for implementing event-driven architectures in .NET applications. It defines the contracts for events, event publishers, and event subscribers, enabling decoupled communication between components using the publish/subscribe pattern.

- **IEvent**: Marker interface for domain events, with timestamp and unique ID
- **IEventPublisher**: Interface for publishing events to subscribers
- **IEventSubscriber<TEvent>**: Interface for handling events
- **EventBase**: Abstract base class for events with sensible defaults
- **DI Extensions**: Helper for registering event handlers

## Table of Contents
- [Getting Started](#getting-started)
- [Interfaces & Base Classes](#interfaces--base-classes)
- [Dependency Injection](#dependency-injection)
- [Usage Example](#usage-example)
- [Best Practices](#best-practices)
- [Extending](#extending)
- [Contributing](#contributing)
- [License](#license)

## Getting Started

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

Add a reference to this project or package in your solution:

```sh
dotnet add package FluentCMS.EventBus.Abstractions
```

## Interfaces & Base Classes

### IEvent
```csharp
public interface IEvent
{
    DateTimeOffset OccurredAt { get; }
    Guid EventId { get; }
}
```

### EventBase
```csharp
public abstract class EventBase : IEvent
{
    public DateTimeOffset OccurredAt { get; init; }
    public Guid EventId { get; init; }
    protected EventBase() { /* ... */ }
}
```

### IEventPublisher
```csharp
public interface IEventPublisher
{
    Task Publish<TEvent>(TEvent data, CancellationToken cancellationToken = default) where TEvent : class, IEvent;
}
```

### IEventSubscriber<TEvent>
```csharp
public interface IEventSubscriber<TEvent> where TEvent : class, IEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken = default);
}
```

## Dependency Injection

Register event handlers using the provided extension:

```csharp
services.AddEventHandler<UserRegisteredEvent, SendWelcomeEmailSubscriber>();
```

This registers `SendWelcomeEmailSubscriber` as a scoped handler for `UserRegisteredEvent`.

## Usage Example

```csharp
// Define an event
global using FluentCMS.EventBus.Abstractions;

public class UserRegisteredEvent : EventBase { public string UserId { get; set; } }

// Implement a subscriber
public class SendWelcomeEmailSubscriber : IEventSubscriber<UserRegisteredEvent>
{
    public Task Handle(UserRegisteredEvent @event, CancellationToken cancellationToken)
    {
        // Send welcome email logic
        return Task.CompletedTask;
    }
}

// Register the handler
services.AddEventHandler<UserRegisteredEvent, SendWelcomeEmailSubscriber>();

// Publish an event (implementation required)
await eventPublisher.Publish(new UserRegisteredEvent { UserId = "123" });
```

## Best Practices
- Use `EventBase` for consistent event metadata.
- Register handlers with appropriate lifetimes (scoped by default).
- Keep event handlers focused and non-blocking.
- Use unique event types for different domain actions.

## Extending
- Implement custom event publishers or subscribers as needed.
- Extend `EventBase` for additional metadata.

## Contributing
Contributions are welcome! Please open issues or submit pull requests for improvements or bug fixes.

## License
MIT License

---

**FluentCMS.EventBus.Abstractions** is part of the [FluentCMS](https://github.com/fluentcms/FluentCMS) ecosystem.
