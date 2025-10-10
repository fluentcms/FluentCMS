# Plugin System Architecture

## Table of Contents

- [Overview](#overview)
- [Design Principles](#design-principles)
- [System Components](#system-components)
- [Three-Phase Initialization](#three-phase-initialization)
- [Dependency Resolution](#dependency-resolution)
- [Plugin Communication](#plugin-communication)
- [Error Handling Strategy](#error-handling-strategy)
- [Resource Management](#resource-management)
- [Security Considerations](#security-considerations)

## Overview

The FluentCMS Plugin System is designed as an enterprise-grade, modular architecture where the host application serves as a minimal infrastructure shell. All business functionality is delivered through independent, self-contained plugins.

### Core Philosophy

- **Host = Infrastructure**: Database, logging, event bus, plugin system
- **Plugins = Business Logic**: Independent domains communicating via events
- **Loose Coupling**: No direct references between plugins (except via shared contracts)
- **Fail-Safe**: Graceful degradation when plugins fail
- **Developer-Friendly**: Convention over configuration, sensible defaults

## Design Principles

### 1. Separation of Concerns

```
┌─────────────────────────────────────────┐
│           Host Application              │
│  • Database Configuration               │
│  • Logging Infrastructure               │
│  • Event Bus (MediatR)                  │
│  • Plugin System                        │
└─────────────────────────────────────────┘
                    ▲
                    │ Provides Infrastructure
                    │
        ┌───────────┴───────────┐
        │                       │
┌───────▼────────┐    ┌────────▼────────┐
│  Plugin A      │    │  Plugin B       │
│  (CRM)         │    │  (Accounting)   │
│                │    │                 │
│  Business      │    │  Business       │
│  Logic         │    │  Logic          │
└────────────────┘    └─────────────────┘
        │                       │
        └───────────┬───────────┘
                    │ Event-Driven Communication
                    ▼
           ┌────────────────┐
           │   Event Bus    │
           └────────────────┘
```

### 2. Event-Driven Architecture

Plugins communicate exclusively through events to maintain loose coupling:

- **No Direct References**: Plugin A doesn't reference Plugin B
- **Publish/Subscribe**: Events broadcast to interested subscribers
- **Type Safety**: Shared event contracts ensure compile-time validation
- **Decoupled Timing**: Publishers don't wait for subscribers

### 3. Convention Over Configuration

```csharp
// Minimal plugin - uses all defaults
[Plugin]
public class TextWidgetStartup : IPluginStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITextWidget, TextWidget>();
    }
    
    public void Configure(IApplicationBuilder app, IServiceProvider provider)
    {
        // No middleware needed
    }
}

// Metadata automatically derived:
// Name: "FluentCMS.Plugins.TextWidget" (from assembly name)
// Version: "1.0.0" (from assembly version)
// Priorities: 100 (default)
```

### 4. Compile-Time Safety

- Dependencies via project references (not strings)
- Type-safe event contracts
- No runtime assembly loading
- Build fails if dependencies missing

## System Components

### Core Library Structure

```
FluentCMS.Infrastructure.Plugins/
├── Abstractions/
│   ├── IPluginStartup.cs           # Main plugin interface
│   ├── PluginAttribute.cs          # Discovery marker
│   ├── PluginMetadata.cs           # Plugin metadata model
│   └── Lifecycle/
│       ├── PluginLoadingEvent.cs
│       ├── PluginServicesConfiguredEvent.cs
│       ├── PluginConfiguringEvent.cs
│       ├── PluginConfiguredEvent.cs
│       ├── ApplicationStartedEvent.cs
│       └── ApplicationStoppingEvent.cs
│
├── Discovery/
│   ├── PluginScanner.cs            # Assembly scanning
│   ├── PluginValidator.cs          # Validation logic
│   └── DependencyGraphBuilder.cs   # Dependency resolution
│
├── Loading/
│   ├── PluginLoader.cs             # Plugin initialization
│   ├── ServiceRegistrar.cs         # Phase 2: ConfigureServices
│   └── PipelineConfigurator.cs     # Phase 3: Configure
│
├── Lifecycle/
│   ├── LifecycleEventPublisher.cs  # Lifecycle event management
│   └── PluginStateManager.cs       # Track plugin states
│
├── Registry/
│   ├── IPluginRegistry.cs          # Query interface
│   ├── PluginRegistry.cs           # Implementation
│   └── PluginInfo.cs               # Runtime plugin info
│
├── Resources/
│   ├── ResourceQuotaMonitor.cs     # Resource tracking
│   └── PluginMetrics.cs            # Performance metrics
│
└── HealthChecks/
    ├── PluginHealthCheckAggregator.cs
    └── PluginHealthCheck.cs
```

## Three-Phase Initialization

The plugin system uses a carefully orchestrated three-phase initialization to ensure correct ordering and dependency resolution.

### Phase 1: Discovery & Loading

**Purpose**: Find plugins, validate dependencies, establish load order

```
┌──────────────────────────────────────┐
│  1. Scan Assemblies                  │
│     • Filter by ScanAssemblyPatterns │
│     • Find [Plugin] attributes       │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  2. Create Plugin Instances          │
│     • Instantiate IPluginStartup     │
│     • Extract metadata               │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  3. Build Dependency Graph           │
│     • Scan assembly references       │
│     • Detect plugin dependencies     │
│     • Topological sort               │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  4. Validate                         │
│     • All dependencies exist?        │
│     • No circular dependencies?      │
│     • Unique plugin names?           │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  5. Load Order Determined            │
│     • Dependency-based order         │
│     • Fire PluginLoadingEvent        │
└──────────────────────────────────────┘
```

**Example**:

```
Assemblies Scanned:
- FluentCMS.Plugins.Identity.dll [Plugin] → IdentityStartup
- FluentCMS.Plugins.CRM.dll [Plugin] → CRMStartup (references Identity)
- FluentCMS.Plugins.Accounting.dll [Plugin] → AccountingStartup (references CRM)

Dependency Graph:
Identity → CRM → Accounting

Load Order: Identity, CRM, Accounting
```

### Phase 2: ConfigureServices

**Purpose**: Register services in DI container in priority order

```
┌──────────────────────────────────────┐
│  1. Sort by ConfigureServicesPriority│
│     • Lower priority = earlier       │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  2. For Each Plugin (in order)       │
│     • Fire PluginConfiguringEvent    │
│     • Call ConfigureServices()       │
│     • Fire PluginConfiguredEvent     │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  3. Build IServiceProvider           │
│     • All services registered        │
│     • DI container ready             │
└──────────────────────────────────────┘
```

**Priority Example**:

```csharp
// Identity Plugin - needs to register auth services early
[Plugin]
public class IdentityStartup : IPluginStartup
{
    public override int ConfigureServicesPriority => 10; // Early
    
    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication();
        services.AddAuthorization();
    }
}

// CRM Plugin - standard priority
[Plugin]
public class CRMStartup : IPluginStartup
{
    public override int ConfigureServicesPriority => 100; // Default
    
    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<ICRMService, CRMService>();
    }
}

// Registration Order: Identity (10) → CRM (100)
```

### Phase 3: Configure

**Purpose**: Setup middleware pipeline and finalization in priority order

```
┌──────────────────────────────────────┐
│  1. Sort by ConfigurePriority        │
│     • Lower priority = earlier       │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  2. For Each Plugin (in order)       │
│     • Fire PluginConfiguringEvent    │
│     • Call Configure()               │
│     • Fire PluginConfiguredEvent     │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│  3. Fire ApplicationStartedEvent     │
│     • All plugins active             │
│     • Application ready              │
└──────────────────────────────────────┘
```

**Critical for Middleware Order**:

```csharp
[Plugin]
public class AuthenticationStartup : IPluginStartup
{
    public override int ConfigurePriority => 10; // Must run early
    
    public void Configure(IApplicationBuilder app, IServiceProvider provider)
    {
        app.UseAuthentication();
    }
}

[Plugin]
public class AuthorizationStartup : IPluginStartup
{
    public override int ConfigurePriority => 20; // After authentication
    
    public void Configure(IApplicationBuilder app, IServiceProvider provider)
    {
        app.UseAuthorization();
    }
}

[Plugin]
public class APIStartup : IPluginStartup
{
    public override int ConfigurePriority => 100; // After auth/authz
    
    public void Configure(IApplicationBuilder app, IServiceProvider provider)
    {
        app.MapControllers();
    }
}

// Pipeline Order: Authentication → Authorization → API
```

## Dependency Resolution

### Automatic Detection

Dependencies are automatically detected from project references:

```csharp
// Algorithm
foreach (var pluginAssembly in discoveredPlugins)
{
    var referencedAssemblies = pluginAssembly.GetReferencedAssemblies();
    
    foreach (var reference in referencedAssemblies)
    {
        var refAssembly = Assembly.Load(reference);
        
        if (HasPluginAttribute(refAssembly))
        {
            // This is a plugin dependency
            dependencyGraph.AddEdge(pluginAssembly, refAssembly);
        }
    }
}

// Topological sort
var loadOrder = dependencyGraph.TopologicalSort();
```

### Circular Dependency Detection

```
Plugin A → Plugin B → Plugin C → Plugin A (CIRCULAR!)

Detection:
1. Build dependency graph
2. Attempt topological sort
3. If cycle detected: throw PluginCircularDependencyException
4. Exception includes full cycle path for debugging
```

### Missing Dependency Handling

```csharp
// Validation during Phase 1
foreach (var plugin in plugins)
{
    foreach (var dependency in plugin.Dependencies)
    {
        if (!loadedPlugins.Contains(dependency))
        {
            throw new PluginDependencyMissingException(
                $"Plugin '{plugin.Name}' depends on '{dependency}' which is not loaded.");
        }
    }
}
```

## Plugin Communication

### Event-Based Communication

```
┌─────────────┐
│  Plugin A   │
│             │
│  Publishes  │
│  Event      │
└──────┬──────┘
       │
       ▼
┌──────────────────┐
│   Event Bus      │
│   (MediatR)      │
└──────┬───────┬───┘
       │       │
       ▼       ▼
┌──────────┐ ┌──────────┐
│ Plugin B │ │ Plugin C │
│          │ │          │
│ Handles  │ │ Handles  │
└──────────┘ └──────────┘
```

### Event Contract Sharing

```
Project Structure:

FluentCMS.Plugins.CRM/
├── FluentCMS.Plugins.CRM.csproj
└── FluentCMS.Plugins.CRM.Contracts/
    └── Events/
        └── CustomerCreatedEvent.cs

FluentCMS.Plugins.Accounting/
├── FluentCMS.Plugins.Accounting.csproj (references CRM.Contracts)
└── EventHandlers/
    └── CustomerCreatedEventHandler.cs
```

### API Gateway Pattern

Plugins access host features through abstraction interfaces:

```
Host Provides:
├── IEventPublisher          (publish domain events)
├── IConfiguration           (plugin configuration)
├── ILogger<T>               (structured logging)
├── IPluginRegistry          (query loaded plugins)
├── IHttpClientFactory       (HTTP calls)
└── Company Abstractions     (IEmailSender, IFileStorage, etc.)

Plugin Consumes via DI:
public class InvoiceService
{
    private readonly IEventPublisher _eventPublisher;
    private readonly IEmailSender _emailSender;
    
    public InvoiceService(IEventPublisher eventPublisher, IEmailSender emailSender)
    {
        _eventPublisher = eventPublisher;
        _emailSender = emailSender;
    }
}
```

## Error Handling Strategy

### Startup Errors

```csharp
// Configuration
options.IgnoreErrors = false; // Development: fail fast
options.IgnoreErrors = true;  // Production: graceful degradation

// Behavior
if (plugin.ConfigureServices() throws exception)
{
    if (options.IgnoreErrors)
    {
        _logger.LogError(exception, "Plugin {PluginName} failed to configure services", plugin.Name);
        pluginRegistry.MarkAsFailed(plugin);
        continue; // Load other plugins
    }
    else
    {
        throw; // Fail application startup
    }
}
```

### Runtime Errors

- **Event Handlers**: Exceptions logged, event bus continues
- **Middleware**: Standard ASP.NET error handling applies
- **Services**: Normal DI lifetime exceptions apply

## Resource Management

### Resource Quota Tracking

```csharp
public class ResourceQuotaMonitor
{
    // Track per plugin:
    // - Memory usage
    // - CPU time
    // - Database connections
    // - HTTP requests
    
    public void RecordPluginMetric(string pluginName, string metricName, double value)
    {
        // Store in time-series format
        // Expose via /api/plugins/metrics endpoint
    }
}
```

### Health Checks

```csharp
// Plugin implements
public class MyPluginHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Check database connectivity
        // Check external service availability
        // Check resource usage
        
        return Task.FromResult(HealthCheckResult.Healthy());
    }
}

// Host aggregates
GET /health/plugins
{
    "status": "Healthy",
    "plugins": [
        { "name": "CRM", "status": "Healthy" },
        { "name": "Accounting", "status": "Degraded", "reason": "High latency" }
    ]
}
```

## Security Considerations

### API Gateway Pattern

All host features accessed through controlled interfaces:

```
Plugin Code:
├── ✅ Can: Inject IEventPublisher, ILogger, IEmailSender
├── ✅ Can: Access plugin's configuration section
├── ❌ Cannot: Access other plugin's services (unless explicitly shared)
├── ❌ Cannot: Access internal host implementation details
└── ❌ Cannot: Bypass security boundaries
```

### Shared Context

Plugins share the main application context:

- **Benefit**: Tight integration, no serialization overhead
- **Risk**: Misbehaving plugin can affect host
- **Mitigation**: Code reviews, testing, monitoring

### Resource Quotas

Monitor and limit plugin resource usage:

```
Tracked Metrics:
- Memory consumption
- CPU usage
- Database connections
- HTTP requests
- Event publication rate

Alerts:
- Warn if plugin exceeds threshold
- Log excessive resource usage
- Health check reports degraded status
```

## Performance Considerations

### Plugin Loading

- **One-time cost**: Only at application startup
- **Optimizations**: Parallel plugin discovery, lazy metadata loading
- **Trade-off**: Favor correctness over startup speed

### Event Handling

- **Async by default**: All event handlers are async
- **No blocking**: Publishers don't wait for subscribers
- **Performance**: MediatR handles pipeline efficiently

### DI Resolution

- **Standard .NET DI**: No special overhead
- **Service lifetimes**: Follow normal scoped/transient/singleton patterns
- **Best practice**: Cache resolved services when appropriate

## Extensibility Points

The architecture supports future enhancements:

1. **Environment-Based Loading**: Filter plugins by environment
2. **Communication Tracing**: Add correlation tracking
3. **Hot Reload**: Scoped service factory pattern
4. **Plugin Marketplace**: Load plugins from NuGet/external sources
5. **Versioning**: Side-by-side plugin versions
6. **Sandboxing**: AssemblyLoadContext isolation for untrusted plugins

---

**Next**: [Plugin Development Guide](./PLUGIN-DEVELOPMENT-GUIDE.md)
