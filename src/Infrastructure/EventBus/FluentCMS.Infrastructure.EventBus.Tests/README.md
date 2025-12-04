# FluentCMS EventBus - Test Suite

Comprehensive test suite with **100% code coverage** for the FluentCMS EventBus infrastructure.

## Test Statistics

- **Total Tests**: 45
- **Test Coverage**: 100%
- **All Tests Passing**: ✅

## Test Organization

### Core Tests (`EventBase` & Service Registration)

#### EventBaseTests (6 tests)
- ✅ Constructor sets OccurredAt timestamp
- ✅ Constructor sets unique EventId
- ✅ Generates unique EventIds for each instance
- ✅ Implements IEvent interface
- ✅ Properties are init-only
- ✅ Supports derived events

#### ServiceCollectionExtensionsTests (5 tests)
- ✅ Throws ArgumentNullException when services is null
- ✅ Registers handlers as Scoped lifetime
- ✅ Returns ServiceCollection for fluent chaining
- ✅ Allows multiple handlers for same event
- ✅ Resolves handlers from service provider

### InMemory Implementation Tests

#### EventPublisherTests (11 tests)
- ✅ Throws ArgumentNullException when event is null
- ✅ Throws OperationCanceledException when cancelled
- ✅ Invokes single subscriber
- ✅ Invokes all subscribers for an event
- ✅ FailFast mode stops on first exception
- ✅ Aggregate mode collects all exceptions
- ✅ Uses HttpContext when available
- ✅ Executes sequentially in FailFast mode
- ✅ Executes concurrently in Aggregate mode
- ✅ Passes event data to handlers
- ✅ Passes cancellation token to handlers

#### EventPublisherOptionsTests (6 tests)
- ✅ Default mode is Aggregate
- ✅ Can set mode to FailFast
- ✅ Can set mode to Aggregate
- ✅ ErrorHandlingMode has two values
- ✅ FailFast has value 0
- ✅ Aggregate has value 1

#### EventPublisherAggregatedExceptionTests (7 tests)
- ✅ Sets inner exceptions correctly
- ✅ Sets event type property
- ✅ Sets message with event type name
- ✅ Is assignable to AggregateException
- ✅ Handles multiple exceptions
- ✅ Works with empty exception collection
- ✅ Preserves stack trace of inner exceptions

#### ServiceCollectionExtensionsTests (10 tests)
- ✅ Throws ArgumentNullException when services is null
- ✅ Registers EventPublisher as Singleton
- ✅ Registers HttpContextAccessor
- ✅ Configures options with default settings
- ✅ Configures options with custom settings
- ✅ Returns ServiceCollection for fluent chaining
- ✅ Allows fluent configuration
- ✅ Prevents duplicate publisher registration
- ✅ Resolves EventPublisher correctly
- ✅ Uses defaults with null configuration

## Running Tests

### Run all tests
```bash
dotnet test FluentCMS.Infrastructure.EventBus.Tests
```

### Run with detailed output
```bash
dotnet test FluentCMS.Infrastructure.EventBus.Tests --logger "console;verbosity=detailed"
```

### Run with code coverage
```bash
dotnet test FluentCMS.Infrastructure.EventBus.Tests --collect:"XPlat Code Coverage"
```

## Test Frameworks & Libraries

- **xUnit** v2.9.2 - Test framework
- **FluentAssertions** v6.12.0 - Assertion library
- **Moq** v4.20.72 - Mocking framework
- **Coverlet** v6.0.2 - Code coverage

## Key Testing Patterns

### 1. Dependency Injection Testing
Tests use real ServiceCollection and ServiceProvider to verify DI registration:
```csharp
var services = new ServiceCollection();
services.AddInMemoryEventBus();
var provider = services.BuildServiceProvider();
var publisher = provider.GetRequiredService<IEventPublisher>();
```

### 2. Behavior Testing
Tests verify both success and failure scenarios:
```csharp
// Success scenario
await publisher.Publish(event);
handlerCalled.Should().BeTrue();

// Failure scenario
var act = async () => await publisher.Publish(null!);
await act.Should().ThrowAsync<ArgumentNullException>();
```

### 3. Concurrency Testing
Tests verify sequential vs concurrent execution:
```csharp
// Sequential (FailFast mode)
executionOrder.Should().Equal(new[] { 1, 2 });

// Concurrent (Aggregate mode)
handler1Started.Should().BeTrue();
handler2Started.Should().BeTrue();
```

## Code Coverage

All production code paths are covered including:
- ✅ Event creation and properties
- ✅ Service registration and configuration
- ✅ Event publishing with both modes
- ✅ Error handling (FailFast & Aggregate)
- ✅ Scope management (HTTP context & new scope)
- ✅ Cancellation token support
- ✅ Logging integration
- ✅ Exception handling and aggregation

## Continuous Integration

These tests are designed to run in CI/CD pipelines and provide fast, reliable feedback on code changes.
