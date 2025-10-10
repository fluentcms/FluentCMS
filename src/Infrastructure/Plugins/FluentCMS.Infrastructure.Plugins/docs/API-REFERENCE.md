# API Reference

## Table of Contents

- [Core Interfaces](#core-interfaces)
- [Attributes](#attributes)
- [Lifecycle Events](#lifecycle-events)
- [Plugin Registry](#plugin-registry)
- [Configuration Options](#configuration-options)
- [Extension Methods](#extension-methods)
- [Event Bus](#event-bus)

## Core Interfaces

### IPluginStartup

The main interface that all plugins must implement.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions;

public interface IPluginStartup
{
    /// <summary>
    /// Plugin name (defaults to assembly name)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Plugin version (defaults to assembly version)
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Priority for ConfigureServices phase (lower = earlier, default: 100)
    /// </summary>
    int ConfigureServicesPriority { get; }

    /// <summary>
    /// Priority for Configure phase (lower = earlier, default: 100)
    /// </summary>
    int ConfigurePriority { get; }

    /// <summary>
    /// Configure services in the DI container.
    /// The provided configuration is scoped to the plugin's "Plugins:PluginName" section.
    /// </summary>
    void ConfigureServices(
        IServiceCollection services, 
        IConfiguration configuration);

    /// <summary>
    /// Configure the application pipeline and finalization
    /// </summary>
    void Configure(
        IApplicationBuilder app);
}
```

**Default Implementation:**

```csharp
public abstract class PluginStartupBase : IPluginStartup
{
    public virtual string Name => GetType().Assembly.GetName().Name ?? "Unknown";
    public virtual string Version => GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0";
    public virtual int ConfigureServicesPriority => 100;
    public virtual int ConfigurePriority => 100;

    public abstract void ConfigureServices(IServiceCollection services, IConfiguration configuration);
    public virtual void Configure(IApplicationBuilder app) { }
}
```

**Usage:**

```csharp
[Plugin]
public class MyPluginStartup : IPluginStartup
{
    // Override only what you need
    public override int ConfigurePriority => 10;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMyService, MyService>();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseMiddleware<MyMiddleware>();
    }
}
```

### IPluginRegistry

Query interface for accessing loaded plugin information at runtime.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions;

public interface IPluginRegistry
{
    /// <summary>
    /// Get all loaded plugins
    /// </summary>
    IReadOnlyList<PluginInfo> GetAllPlugins();

    /// <summary>
    /// Get plugin by name
    /// </summary>
    PluginInfo? GetPlugin(string name);

    /// <summary>
    /// Check if plugin is loaded
    /// </summary>
    bool IsPluginLoaded(string name);

    /// <summary>
    /// Get plugin status
    /// </summary>
    PluginStatus GetPluginStatus(string name);

    /// <summary>
    /// Get plugins with specific status
    /// </summary>
    IReadOnlyList<PluginInfo> GetPluginsByStatus(PluginStatus status);
}
```

**Usage:**

```csharp
public class MyService
{
    private readonly IPluginRegistry _pluginRegistry;

    public MyService(IPluginRegistry pluginRegistry)
    {
        _pluginRegistry = pluginRegistry;
    }

    public void CheckDependency()
    {
        if (_pluginRegistry.IsPluginLoaded("Identity Plugin"))
        {
            // Proceed with logic that depends on Identity plugin
        }
    }
}
```

### PluginInfo

Contains runtime information about a loaded plugin.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions;

public class PluginInfo
{
    /// <summary>
    /// Plugin name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Plugin version
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Assembly name
    /// </summary>
    public string AssemblyName { get; init; } = string.Empty;

    /// <summary>
    /// Plugin dependencies (other plugin names)
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Current status
    /// </summary>
    public PluginStatus Status { get; set; }

    /// <summary>
    /// When the plugin was loaded
    /// </summary>
    public DateTimeOffset LoadedAt { get; init; }

    /// <summary>
    /// Error message if plugin failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// ConfigureServices priority
    /// </summary>
    public int ConfigureServicesPriority { get; init; }

    /// <summary>
    /// Configure priority
    /// </summary>
    public int ConfigurePriority { get; init; }
}
```

### PluginStatus

Enumeration of possible plugin states.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions;

public enum PluginStatus
{
    /// <summary>
    /// Plugin is discovered but not yet loaded
    /// </summary>
    Discovered,

    /// <summary>
    /// Plugin is currently loading
    /// </summary>
    Loading,

    /// <summary>
    /// ConfigureServices has been called
    /// </summary>
    ServicesConfigured,

    /// <summary>
    /// Configure has been called, plugin is active
    /// </summary>
    Active,

    /// <summary>
    /// Plugin failed to load or configure
    /// </summary>
    Failed,

    /// <summary>
    /// Plugin is being stopped
    /// </summary>
    Stopping,

    /// <summary>
    /// Plugin has been stopped
    /// </summary>
    Stopped
}
```

## Attributes

### PluginAttribute

Marks a class as a plugin startup class for discovery.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PluginAttribute : Attribute
{
    // Empty attribute used only for discovery
}
```

**Usage:**

```csharp
[Plugin]
public class MyPluginStartup : IPluginStartup
{
    // Implementation
}
```

## Lifecycle Events

All plugin lifecycle events implement `IPluginLifecycleEvent`, which provides a clear distinction from general domain events.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions.Lifecycle;

/// <summary>
/// Marker interface for all plugin lifecycle events.
/// Inherits from the core IEvent interface.
/// </summary>
public interface IPluginLifecycleEvent : IEvent
{
}
```

### PluginLoadingEvent

Published when a plugin is about to be loaded.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions.Lifecycle;

public class PluginLoadingEvent : IPluginLifecycleEvent
{
    public string PluginName { get; init; } = string.Empty;
    public string AssemblyName { get; init; } = string.Empty;
    public Guid EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
```

### PluginServicesConfiguredEvent

Published after a plugin's `ConfigureServices` method completes.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions.Lifecycle;

public class PluginServicesConfiguredEvent : IPluginLifecycleEvent
{
    public string PluginName { get; init; } = string.Empty;
    public Guid EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
```

### PluginConfiguringEvent

Published before a plugin's `Configure` method is called.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions.Lifecycle;

public class PluginConfiguringEvent : IPluginLifecycleEvent
{
    public string PluginName { get; init; } = string.Empty;
    public Guid EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
```

### PluginConfiguredEvent

Published after a plugin's `Configure` method completes.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions.Lifecycle;

public class PluginConfiguredEvent : IPluginLifecycleEvent
{
    public string PluginName { get; init; } = string.Empty;
    public Guid EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
```

### ApplicationStartedEvent

Published after all plugins are configured and application is ready.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions.Lifecycle;

public class ApplicationStartedEvent : IPluginLifecycleEvent
{
    public int TotalPluginsLoaded { get; init; }
    public Guid EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
```

### ApplicationStoppingEvent

Published when the application is shutting down.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions.Lifecycle;

public class ApplicationStoppingEvent : IPluginLifecycleEvent
{
    public Guid EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
```

**Subscribing to Lifecycle Events:**

```csharp
public class MyLifecycleHandler : IEventSubscriber<ApplicationStartedEvent>
{
    private readonly ILogger<MyLifecycleHandler> _logger;

    public MyLifecycleHandler(ILogger<MyLifecycleHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(ApplicationStartedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Application started with {Count} plugins", 
            domainEvent.TotalPluginsLoaded);
        await Task.CompletedTask;
    }
}
```

## Plugin Registry

### IPluginRegistry Methods

#### GetAllPlugins

```csharp
IReadOnlyList<PluginInfo> GetAllPlugins();
```

Returns all loaded plugins with their current status.

**Example:**

```csharp
var plugins = _pluginRegistry.GetAllPlugins();
foreach (var plugin in plugins)
{
    Console.WriteLine($"{plugin.Name} - {plugin.Status}");
}
```

#### GetPlugin

```csharp
PluginInfo? GetPlugin(string name);
```

Retrieves a specific plugin by name, returns `null` if not found.

**Example:**

```csharp
var crmPlugin = _pluginRegistry.GetPlugin("CRM Plugin");
if (crmPlugin != null)
{
    Console.WriteLine($"CRM Plugin Status: {crmPlugin.Status}");
}
```

#### IsPluginLoaded

```csharp
bool IsPluginLoaded(string name);
```

Quick check if a plugin is loaded and active.

**Example:**

```csharp
if (_pluginRegistry.IsPluginLoaded("Identity Plugin"))
{
    // Use identity features
}
```

#### GetPluginStatus

```csharp
PluginStatus GetPluginStatus(string name);
```

Gets the current status of a plugin.

**Example:**

```csharp
var status = _pluginRegistry.GetPluginStatus("Accounting Plugin");
if (status == PluginStatus.Failed)
{
    _logger.LogWarning("Accounting plugin failed to load");
}
```

#### GetPluginsByStatus

```csharp
IReadOnlyList<PluginInfo> GetPluginsByStatus(PluginStatus status);
```

Gets all plugins with a specific status.

**Example:**

```csharp
var failedPlugins = _pluginRegistry.GetPluginsByStatus(PluginStatus.Failed);
if (failedPlugins.Any())
{
    _logger.LogError("Failed plugins: {Plugins}", 
        string.Join(", ", failedPlugins.Select(p => p.Name)));
}
```

## Configuration Options

### PluginSystemOptions

Configuration options for the plugin system.

```csharp
namespace FluentCMS.Infrastructure.Plugins;

public class PluginSystemOptions
{
    /// <summary>
    /// Assembly name patterns for plugin scanning
    /// Default: ["FluentCMS.Plugins.*"]
    /// </summary>
    public string[] ScanAssemblyPatterns { get; set; } = new[] { "FluentCMS.Plugins.*" };

    /// <summary>
    /// Whether to ignore plugin errors and continue loading other plugins
    /// Default: false (fail fast)
    /// </summary>
    public bool IgnoreErrors { get; set; } = false;

    /// <summary>
    /// Enable detailed logging for plugin loading
    /// Default: false
    /// </summary>
    public bool EnableVerboseLogging { get; set; } = false;

    /// <summary>
    /// Enable resource quota monitoring
    /// Default: true
    /// </summary>
    public bool EnableResourceMonitoring { get; set; } = true;

    /// <summary>
    /// Parallel plugin discovery (faster startup)
    /// Default: true
    /// </summary>
    public bool EnableParallelDiscovery { get; set; } = true;
}
```

**Usage:**

```csharp
builder.Services.AddPluginSystem(options =>
{
    options.ScanAssemblyPatterns = new[] 
    { 
        "FluentCMS.Plugins.*", 
        "FluentCMS.Api.Plugins.*",
        "MyCompany.Plugins.*"
    };
    options.IgnoreErrors = true; // Production: graceful degradation
    options.EnableVerboseLogging = builder.Environment.IsDevelopment();
    options.EnableResourceMonitoring = true;
});
```

## Extension Methods

### AddPluginSystem

Registers the plugin system in the DI container.

```csharp
public static IServiceCollection AddPluginSystem(
    this IServiceCollection services,
    Action<PluginSystemOptions>? configure = null)
```

**Example:**

```csharp
builder.Services.AddPluginSystem(options =>
{
    options.ScanAssemblyPatterns = new[] { "*.Plugins.*" };
    options.IgnoreErrors = false;
});
```

### UsePlugins

Configures the application pipeline for plugins.

```csharp
public static IApplicationBuilder UsePlugins(
    this IApplicationBuilder app)
```

**Example:**

```csharp
var app = builder.Build();
app.UsePlugins();
app.Run();
```

## Event Bus

### IEvent

Base interface for all events.

```csharp
namespace FluentCMS.Infrastructure.EventBus.Abstractions;

public interface IEvent
{
    /// <summary>
    /// When the event occurred
    /// </summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Unique event identifier
    /// </summary>
    Guid EventId { get; }
}
```

### IEventPublisher

Interface for publishing events.

```csharp
namespace FluentCMS.Infrastructure.EventBus.Abstractions;

public interface IEventPublisher
{
    /// <summary>
    /// Publish a domain event to all subscribers
    /// </summary>
    Task Publish<TEvent>(
        TEvent data, 
        CancellationToken cancellationToken = default) 
        where TEvent : class, IEvent;
}
```

**Usage:**

```csharp
public class CustomerService
{
    private readonly IEventPublisher _eventPublisher;

    public CustomerService(IEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;
    }

    public async Task CreateCustomer(Customer customer, CancellationToken cancellationToken = default)
    {
        // Save customer
        
        // Publish event
        await _eventPublisher.Publish(new CustomerCreatedEvent
        {
            CustomerId = customer.Id,
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }
}
```

### IEventSubscriber<TEvent>

Interface for subscribing to events.

```csharp
namespace FluentCMS.Infrastructure.EventBus.Abstractions;

public interface IEventSubscriber<TEvent> where TEvent : class, IEvent
{
    /// <summary>
    /// Handle the event
    /// </summary>
    Task Handle(
        TEvent domainEvent, 
        CancellationToken cancellationToken = default);
}
```

**Usage:**

```csharp
public class CustomerCreatedEventHandler : IEventSubscriber<CustomerCreatedEvent>
{
    private readonly ILogger<CustomerCreatedEventHandler> _logger;

    public CustomerCreatedEventHandler(ILogger<CustomerCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(CustomerCreatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Customer {Id} created", domainEvent.CustomerId);
        await Task.CompletedTask;
    }
}
```

## Resource Monitoring

### IResourceQuotaMonitor

Interface for tracking plugin resource usage.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions;

public interface IResourceQuotaMonitor
{
    /// <summary>
    /// Record a metric for a plugin
    /// </summary>
    void RecordMetric(string pluginName, string metricName, double value);

    /// <summary>
    /// Get metrics for a plugin
    /// </summary>
    PluginMetrics GetMetrics(string pluginName);

    /// <summary>
    /// Get all plugin metrics
    /// </summary>
    IReadOnlyDictionary<string, PluginMetrics> GetAllMetrics();
}
```

### PluginMetrics

Resource usage metrics for a plugin.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions;

public class PluginMetrics
{
    public string PluginName { get; init; } = string.Empty;
    public double MemoryUsageMB { get; set; }
    public double CpuUsagePercent { get; set; }
    public int DatabaseConnections { get; set; }
    public int HttpRequests { get; set; }
    public int EventsPublished { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}
```

**Usage:**

```csharp
public class PluginMonitoringService
{
    private readonly IResourceQuotaMonitor _monitor;

    public PluginMonitoringService(IResourceQuotaMonitor monitor)
    {
        _monitor = monitor;
    }

    public void CheckPluginHealth()
    {
        var allMetrics = _monitor.GetAllMetrics();
        
        foreach (var (pluginName, metrics) in allMetrics)
        {
            if (metrics.MemoryUsageMB > 500)
            {
                // Log warning
            }
        }
    }
}
```

## Communication Tracing

To support observability and debugging across plugin boundaries, a `TraceContext` object can be used to correlate events within a single logical operation.

### TraceContext

Carries correlation information across a chain of events.

```csharp
namespace FluentCMS.Infrastructure.Plugins.Abstractions.Tracing;

public class TraceContext
{
    /// <summary>
    /// A unique ID for the entire logical operation/request.
    /// It remains constant across all subsequent events in the chain.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// The ID of the event that caused this event to be published.
    /// This allows you to build a direct causal chain (A caused B, B caused C).
    /// </summary>
    public Guid? CausationId { get; init; }

    /// <summary>
    /// The name of the plugin that initiated the operation.
    /// </summary>
    public string InitiatingPlugin { get; init; } = string.Empty;
}
```

### Enhancing Events with Trace Context

The core `IEvent` interface can be extended to include this context, allowing the event bus to automatically propagate it.

```csharp
// Example of an enhanced IEvent from a shared contracts library
public interface IEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
    TraceContext? TraceInfo { get; set; }
}
```

---

**Next**: [Implementation Plan](./IMPLEMENTATION-PLAN.md)
