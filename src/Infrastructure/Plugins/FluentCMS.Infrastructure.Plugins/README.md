# FluentCMS Enterprise Plugin System

A robust, enterprise-grade plugin system for .NET 9+ that integrates seamlessly with built-in Dependency Injection (DI) and provides a flexible, event-driven architecture for building modular applications.

## 🎯 Overview

The FluentCMS Plugin System enables you to build highly modular applications where functionality is delivered through independent, self-contained plugins. The host application acts as a minimal infrastructure shell, providing only core services like database configuration, logging, event bus, and the plugin system itself.

### Key Features

- ✅ **Assembly Scanning**: Automatic plugin discovery via `[Plugin]` attribute
- ✅ **Dependency Injection**: Seamless integration with Microsoft.Extensions.DependencyInjection
- ✅ **Event-Driven Communication**: MediatR-based event bus for plugin communication
- ✅ **Three-Phase Initialization**: Discovery → ConfigureServices → Configure
- ✅ **Smart Dependency Resolution**: Automatic dependency detection via project references
- ✅ **Priority-Based Ordering**: Control service registration and middleware pipeline order
- ✅ **Health Monitoring**: Built-in health checks and resource quota tracking
- ✅ **Lifecycle Events**: Standard events for plugin lifecycle management
- ✅ **Flexible Architecture**: Plugins can be business logic, middleware, widgets, or integrations
- ✅ **Production-Ready**: Graceful error handling, validation, and monitoring

## 🏗️ Architecture

### Core Components

```
FluentCMS.Infrastructure.Plugins/
├── Abstractions/         # Interfaces and contracts
├── Discovery/            # Assembly scanning and plugin detection
├── Loading/              # Plugin initialization and lifecycle
├── Lifecycle/            # Event management
├── Registry/             # Plugin tracking and metadata
├── Resources/            # Resource quota monitoring
└── HealthChecks/         # Health check aggregation
```

### Plugin Types Supported

- **Business Domain Plugins**: CRM, Accounting, Inventory, etc.
- **Infrastructure Plugins**: Logging middleware, caching, authentication
- **Widget Plugins**: Text widgets, Markdown renderers, charts
- **Integration Plugins**: External API integrations, third-party services

## 🚀 Quick Start

### Host Application Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add plugin system
builder.Services.AddPluginSystem(options =>
{
    options.ScanAssemblyPatterns = new[] { "FluentCMS.Plugins.*", "FluentCMS.Api.Plugins.*" };
    options.IgnoreErrors = false; // Fail fast in development
});

var app = builder.Build();

// Configure plugins (middleware pipeline)
app.UsePlugins();

app.Run();
```

### Creating a Plugin

```csharp
using FluentCMS.Infrastructure.Plugins.Abstractions;

namespace FluentCMS.Plugins.TextWidget;

[Plugin]
public class TextWidgetStartup : IPluginStartup
{
    // Optional: Override defaults
    public override string Name => "Text Widget Plugin";
    public override int ConfigurePriority => 100;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITextWidgetService, TextWidgetService>();
    }

    public void Configure(IApplicationBuilder app, IServiceProvider provider)
    {
        // Optional: Register middleware if needed
    }
}
```

## 📚 Documentation

- **[Architecture Guide](./docs/ARCHITECTURE.md)** - Detailed system architecture and design decisions
- **[Plugin Development Guide](./docs/PLUGIN-DEVELOPMENT-GUIDE.md)** - Step-by-step guide to creating plugins
- **[API Reference](./docs/API-REFERENCE.md)** - Complete API documentation
- **[Implementation Plan](./docs/IMPLEMENTATION-PLAN.md)** - Development phases and task breakdown
- **[Testing Guide](./docs/TESTING-GUIDE.md)** - Testing strategy and examples
- **[Out of Scope Features](./docs/OUT-OF-SCOPE.md)** - Excluded features and rationale
- **[Examples](./docs/EXAMPLES.md)** - Sample plugins and use cases
- **[Event Catalog](./docs/EVENT_CATALOG.md)** - A central registry for all shared domain events.
- **[Plugin Pull Request Checklist](./docs/PLUGIN_PULL_REQUEST_CHECKLIST.md)** - A checklist to ensure plugin quality and stability.

## 🔧 Core Concepts

### Three-Phase Initialization

1. **Discovery Phase**: Scan assemblies, validate dependencies, build dependency graph
2. **ConfigureServices Phase**: Register services in DI container (priority-ordered)
3. **Configure Phase**: Setup middleware pipeline and finalization (priority-ordered)

### Plugin Communication

Plugins communicate through events using the existing MediatR-based event bus:

```csharp
// Publishing an event
await _eventPublisher.Publish(new CustomerCreatedEvent
{
    CustomerId = customer.Id,
    EventId = Guid.NewGuid(),
    OccurredAt = DateTimeOffset.UtcNow
}, cancellationToken);

// Subscribing to an event
public class CustomerCreatedEventHandler : IEventSubscriber<CustomerCreatedEvent>
{
    public async Task Handle(CustomerCreatedEvent domainEvent, CancellationToken cancellationToken)
    {
        // Handle the event
    }
}
```

### Dependency Management

Plugin dependencies are automatically detected via project references:

```xml
<!-- FluentCMS.Plugins.Accounting.csproj -->
<ItemGroup>
  <ProjectReference Include="../FluentCMS.Plugins.Identity/FluentCMS.Plugins.Identity.csproj" />
  <ProjectReference Include="../FluentCMS.Plugins.CRM.Contracts/FluentCMS.Plugins.CRM.Contracts.csproj" />
</ItemGroup>
```

The plugin system automatically:
- Detects Identity as a dependency
- Ensures Identity loads before Accounting
- Validates all dependencies exist at startup

## 🏥 Health Checks

Each plugin can implement health checks:

```csharp
public class MyPluginHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Check plugin health
        return Task.FromResult(HealthCheckResult.Healthy("Plugin is running"));
    }
}
```

Access health status via: `GET /health/plugins`

## 📊 Monitoring & Metadata

View loaded plugins and their status:

```http
GET /api/plugins

Response:
{
  "plugins": [
    {
      "name": "Text Widget Plugin",
      "version": "1.0.0",
      "status": "Active",
      "dependencies": [],
      "loadedAt": "2025-10-09T20:00:00Z"
    }
  ]
}
```

## 🎯 Best Practices

1. **Single Responsibility**: Each plugin should serve one clear business purpose
2. **Event Contracts**: Share event contracts via separate `.Contracts` projects
3. **Configuration**: Use plugin-specific configuration sections
4. **Error Handling**: Implement proper error handling in plugin code
5. **Health Checks**: Always implement health checks for critical plugins
6. **Logging**: Use structured logging with plugin context
7. **Dependencies**: Only reference plugins you directly use

## 🔮 Future Enhancements

- **Environment-Based Plugin Loading**: Enable/disable plugins per environment
- **Plugin Communication Tracing**: Correlation tracking across plugin boundaries
- **Hot Reload**: Development-mode plugin reloading
- **Admin Dashboard**: UI for plugin management and monitoring

## 📝 License

[Your License Here]

## 🤝 Contributing

[Your Contributing Guidelines Here]

## 📞 Support

[Your Support Information Here]
