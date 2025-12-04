using FluentCMS.Infrastructure.EventBus.Abstractions;
using FluentCMS.Infrastructure.EventBus.Example.Events;
using FluentCMS.Infrastructure.EventBus.Example.Handlers;
using FluentCMS.Infrastructure.EventBus.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Infrastructure.EventBus.Example;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("FluentCMS EventBus Example Application");
        Console.WriteLine("========================================\n");

        // Demo 1: Aggregate Mode (default) - concurrent handler execution
        await DemoAggregateMode();

        Console.WriteLine("\n========================================\n");

        // Demo 2: FailFast Mode - sequential handler execution
        await DemoFailFastMode();

        Console.WriteLine("\n========================================");
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    static async Task DemoAggregateMode()
    {
        Console.WriteLine("DEMO 1: Aggregate Mode (Concurrent Execution)");
        Console.WriteLine("----------------------------------------------");
        Console.WriteLine("In this mode, all handlers run concurrently.");
        Console.WriteLine("If errors occur, they are collected and thrown as AggregateException.\n");

        var services = new ServiceCollection();
        
        // Configure EventBus with Aggregate mode (default)
        services.AddInMemoryEventBus(options =>
        {
            options.Mode = EventPublisherOptions.ErrorHandlingMode.Aggregate;
        });

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Register event handlers for UserRegisteredEvent
        services.AddEventHandler<UserRegisteredEvent, SendWelcomeEmailHandler>();
        services.AddEventHandler<UserRegisteredEvent, CreateUserProfileHandler>();
        services.AddEventHandler<UserRegisteredEvent, LogUserActivityHandler>();

        // Register event handlers for OrderPlacedEvent
        services.AddEventHandler<OrderPlacedEvent, UpdateInventoryHandler>();
        services.AddEventHandler<OrderPlacedEvent, SendOrderConfirmationHandler>();

        var serviceProvider = services.BuildServiceProvider();
        var publisher = serviceProvider.GetRequiredService<IEventPublisher>();

        // Example 1: Publishing UserRegisteredEvent
        Console.WriteLine("\n[Example 1] Publishing UserRegisteredEvent...\n");
        var userEvent = new UserRegisteredEvent
        {
            UserId = "USER-001",
            Email = "john.doe@example.com",
            FullName = "John Doe"
        };

        var startTime = DateTime.Now;
        await publisher.Publish(userEvent);
        var elapsed = DateTime.Now - startTime;
        
        Console.WriteLine($"\n✓ All handlers completed in {elapsed.TotalMilliseconds:F0}ms (concurrent execution)");

        // Example 2: Publishing OrderPlacedEvent
        Console.WriteLine("\n[Example 2] Publishing OrderPlacedEvent...\n");
        var orderEvent = new OrderPlacedEvent
        {
            OrderId = "ORD-2024-001",
            CustomerId = "CUST-456",
            TotalAmount = 299.99m,
            ItemCount = 3
        };

        startTime = DateTime.Now;
        await publisher.Publish(orderEvent);
        elapsed = DateTime.Now - startTime;
        
        Console.WriteLine($"\n✓ All handlers completed in {elapsed.TotalMilliseconds:F0}ms (concurrent execution)");
    }

    static async Task DemoFailFastMode()
    {
        Console.WriteLine("DEMO 2: FailFast Mode (Sequential Execution)");
        Console.WriteLine("----------------------------------------------");
        Console.WriteLine("In this mode, handlers run sequentially in registration order.");
        Console.WriteLine("If an error occurs, execution stops immediately.\n");

        var services = new ServiceCollection();
        
        // Configure EventBus with FailFast mode
        services.AddInMemoryEventBus(options =>
        {
            options.Mode = EventPublisherOptions.ErrorHandlingMode.FailFast;
        });

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Register event handlers in specific order
        services.AddEventHandler<UserRegisteredEvent, SendWelcomeEmailHandler>();
        services.AddEventHandler<UserRegisteredEvent, CreateUserProfileHandler>();
        services.AddEventHandler<UserRegisteredEvent, LogUserActivityHandler>();

        var serviceProvider = services.BuildServiceProvider();
        var publisher = serviceProvider.GetRequiredService<IEventPublisher>();

        // Publishing UserRegisteredEvent with FailFast mode
        Console.WriteLine("\n[Example] Publishing UserRegisteredEvent...\n");
        var userEvent = new UserRegisteredEvent
        {
            UserId = "USER-002",
            Email = "jane.smith@example.com",
            FullName = "Jane Smith"
        };

        var startTime = DateTime.Now;
        await publisher.Publish(userEvent);
        var elapsed = DateTime.Now - startTime;
        
        Console.WriteLine($"\n✓ All handlers completed in {elapsed.TotalMilliseconds:F0}ms (sequential execution)");
        Console.WriteLine("  Note: Sequential execution takes longer than concurrent execution");
    }
}
