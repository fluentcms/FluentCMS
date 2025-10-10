# Plugin Development Guide

## Table of Contents

- [Getting Started](#getting-started)
- [Plugin Structure](#plugin-structure)
- [Creating Your First Plugin](#creating-your-first-plugin)
- [Plugin Lifecycle](#plugin-lifecycle)
- [Service Registration](#service-registration)
- [Configuration Management](#configuration-management)
- [Event Communication](#event-communication)
- [Middleware Registration](#middleware-registration)
- [Health Checks](#health-checks)
- [Best Practices](#best-practices)
- [Common Patterns](#common-patterns)
- [Troubleshooting](#troubleshooting)

## Getting Started

### Prerequisites

- .NET 9+ SDK
- Visual Studio 2022 or JetBrains Rider
- Basic understanding of ASP.NET Core and Dependency Injection

### Project Setup

1. Create a new Class Library project:

```bash
dotnet new classlib -n FluentCMS.Plugins.YourPlugin -f net9.0
```

2. Add required package references:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.App" />
  <ProjectReference Include="../FluentCMS.Infrastructure.Plugins.Abstractions/FluentCMS.Infrastructure.Plugins.Abstractions.csproj" />
</ItemGroup>
```

3. Add dependencies on other plugins (if needed):

```xml
<ItemGroup>
  <ProjectReference Include="../FluentCMS.Plugins.Identity/FluentCMS.Plugins.Identity.csproj" />
  <ProjectReference Include="../FluentCMS.Plugins.CRM.Contracts/FluentCMS.Plugins.CRM.Contracts.csproj" />
</ItemGroup>
```

## Plugin Structure

### Recommended Project Structure

#### Simple Plugin (Widget, Utility)

```
FluentCMS.Plugins.TextWidget/
├── TextWidgetStartup.cs          # Plugin entry point
├── Services/
│   ├── ITextWidgetService.cs
│   └── TextWidgetService.cs
├── Models/
│   └── TextWidgetSettings.cs
└── appsettings.plugin.json       # Optional plugin-specific config
```

#### Business Domain Plugin (CRM, Accounting)

```
FluentCMS.Plugins.CRM/
├── CRMStartup.cs                 # Plugin entry point
├── Features/                      # Vertical slice organization
│   ├── Customers/
│   │   ├── CreateCustomer/
│   │   │   ├── CreateCustomerCommand.cs
│   │   │   ├── CreateCustomerHandler.cs
│   │   │   └── CustomerCreatedEvent.cs
│   │   └── GetCustomer/
│   │       ├── GetCustomerQuery.cs
│   │       └── GetCustomerHandler.cs
├── Data/
│   ├── CRMDbContext.cs
│   └── Models/
│       └── Customer.cs
├── Configuration/
│   └── CRMSettings.cs
└── HealthChecks/
    └── CRMHealthCheck.cs
```

#### Plugin with Shared Contracts

```
FluentCMS.Plugins.CRM/
├── FluentCMS.Plugins.CRM.csproj
└── FluentCMS.Plugins.CRM.Contracts/
    ├── FluentCMS.Plugins.CRM.Contracts.csproj
    └── Events/
        ├── CustomerCreatedEvent.cs
        ├── CustomerUpdatedEvent.cs
        └── CustomerDeletedEvent.cs
```

## Creating Your First Plugin

### Step 1: Create the Startup Class

```csharp
using FluentCMS.Infrastructure.Plugins.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.TextWidget;

[Plugin]
public class TextWidgetStartup : IPluginStartup
{
    // Optional: Override metadata defaults
    public override string Name => "Text Widget Plugin";
    public override string Version => "1.0.0";
    
    // Optional: Control execution order
    public override int ConfigureServicesPriority => 100; // Default
    public override int ConfigurePriority => 100; // Default
    
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register your services
        services.AddScoped<ITextWidgetService, TextWidgetService>();
        
        // Register configuration. The provided 'configuration' is already scoped to this plugin.
        services.Configure<TextWidgetSettings>(configuration);
    }
    
    public void Configure(IApplicationBuilder app, IServiceProvider provider)
    {
        // Optional: Register middleware, endpoints, etc.
    }
}
```

### Step 2: Implement Your Services

```csharp
namespace FluentCMS.Plugins.TextWidget;

public interface ITextWidgetService
{
    Task<string> RenderWidget(string content, CancellationToken cancellationToken = default);
}

public class TextWidgetService : ITextWidgetService
{
    private readonly ILogger<TextWidgetService> _logger;
    private readonly IOptions<TextWidgetSettings> _settings;
    
    public TextWidgetService(
        ILogger<TextWidgetService> logger,
        IOptions<TextWidgetSettings> settings)
    {
        _logger = logger;
        _settings = settings;
    }
    
    public async Task<string> RenderWidget(string content, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rendering text widget");
        
        // Your implementation
        return await Task.FromResult($"<div class='text-widget'>{content}</div>");
    }
}
```

### Step 3: Add Configuration Model

```csharp
namespace FluentCMS.Plugins.TextWidget;

public class TextWidgetSettings
{
    public int MaxLength { get; set; } = 1000;
    public bool AllowHtml { get; set; } = false;
    public string DefaultCssClass { get; set; } = "text-widget";
}
```

### Step 4: Configure in appsettings.json

The host application's `appsettings.json` should contain a `Plugins` section where each plugin has its own configuration key.

```json
{
  "Plugins": {
    "TextWidgetStartup": { // Key should match the plugin's assembly name or overriden Name
      "MaxLength": 2000,
      "AllowHtml": true,
      "DefaultCssClass": "custom-widget"
    }
  }
}
```

## Plugin Lifecycle

### Lifecycle Events

Your plugin can subscribe to lifecycle events to react to system state changes:

```csharp
using FluentCMS.Infrastructure.Plugins.Abstractions.Lifecycle;
using FluentCMS.Infrastructure.EventBus.Abstractions;

namespace FluentCMS.Plugins.MyPlugin;

public class PluginLifecycleHandler : 
    IEventSubscriber<PluginLoadingEvent>,
    IEventSubscriber<PluginConfiguredEvent>,
    IEventSubscriber<ApplicationStartedEvent>,
    IEventSubscriber<ApplicationStoppingEvent>
{
    private readonly ILogger<PluginLifecycleHandler> _logger;
    
    public PluginLifecycleHandler(ILogger<PluginLifecycleHandler> logger)
    {
        _logger = logger;
    }
    
    public async Task Handle(PluginLoadingEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Plugin {PluginName} is loading", domainEvent.PluginName);
        await Task.CompletedTask;
    }
    
    public async Task Handle(PluginConfiguredEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Plugin {PluginName} configured", domainEvent.PluginName);
        await Task.CompletedTask;
    }
    
    public async Task Handle(ApplicationStartedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Application started, initializing plugin resources");
        // Perform startup tasks (e.g., warm cache, connect to external services)
        await Task.CompletedTask;
    }
    
    public async Task Handle(ApplicationStoppingEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Application stopping, cleaning up plugin resources");
        // Perform cleanup tasks
        await Task.CompletedTask;
    }
}
```

Register the lifecycle handler:

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddScoped<PluginLifecycleHandler>();
}
```

## Service Registration

### Basic Service Registration

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // Scoped services (one instance per request)
    services.AddScoped<ICustomerService, CustomerService>();
    
    // Transient services (new instance every time)
    services.AddTransient<IEmailService, EmailService>();
    
    // Singleton services (one instance for application lifetime)
    services.AddSingleton<ICacheService, CacheService>();
}
```

### Registering with Options Pattern

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // The 'configuration' object is pre-scoped to "Plugins:YourPluginName".
    // Bind the entire section to your settings object.
    services.Configure<MyPluginSettings>(configuration);
    
    // Use with IOptions<T>, IOptionsSnapshot<T>, or IOptionsMonitor<T>
    services.AddScoped<IMyService, MyService>();
}

// Consuming service
public class MyService : IMyService
{
    private readonly MyPluginSettings _settings;
    
    public MyService(IOptions<MyPluginSettings> options)
    {
        _settings = options.Value;
    }
}
```

### Registering DbContext

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddDbContext<MyPluginDbContext>(options =>
        options.UseSqlServer(
            configuration.GetConnectionString("MyPluginDatabase"),
            sqlOptions => sqlOptions.MigrationsAssembly(typeof(MyPluginStartup).Assembly.FullName)));
}
```

### Keyed Services (for avoiding conflicts)

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // Register keyed service
    services.AddKeyedScoped<IEmailService, SmtpEmailService>("smtp");
    services.AddKeyedScoped<IEmailService, SendGridEmailService>("sendgrid");
}

// Consume keyed service
public class CustomerService
{
    private readonly IEmailService _emailService;
    
    public CustomerService([FromKeyedServices("smtp")] IEmailService emailService)
    {
        _emailService = emailService;
    }
}
```

## Configuration Management

### Plugin-Specific Configuration Section

Each plugin receives an `IConfiguration` instance that is already scoped to its own section within the main `appsettings.json`. The key for the section should match the plugin's assembly name (or its overridden `Name` property).

**`appsettings.json` in Host:**
```json
{
  "Plugins": {
    "FluentCMS.Plugins.CRM": { // Or whatever the plugin assembly name is
      "ConnectionString": "Server=.;Database=CRM;Trusted_Connection=true;",
      "Features": {
        "EnableNotifications": true,
        "MaxCustomersPerAccount": 1000
      }
    }
  }
}
```

**Plugin Code:**
```csharp
public class CRMSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public CRMFeatures Features { get; set; } = new();
}

public class CRMFeatures
{
    public bool EnableNotifications { get; set; }
    public int MaxCustomersPerAccount { get; set; }
}

// In ConfigureServices
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // The 'configuration' is already scoped, so we bind to it directly.
    services.Configure<CRMSettings>(configuration);
}
```

### Accessing Configuration Directly

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // 'configuration' is already scoped, so you can read values directly.
    var enableFeature = configuration.GetValue<bool>("EnableFeature");
    
    if (enableFeature)
    {
        services.AddScoped<IFeatureService, FeatureService>();
    }
}
```

## Event Communication

### Publishing Events

```csharp
using FluentCMS.Infrastructure.EventBus.Abstractions;

public class CustomerService
{
    private readonly IEventPublisher _eventPublisher;
    
    public CustomerService(IEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;
    }
    
    public async Task CreateCustomer(CreateCustomerDto dto, CancellationToken cancellationToken = default)
    {
        // Create customer
        var customer = new Customer { /* ... */ };
        
        // Publish event
        await _eventPublisher.Publish(new CustomerCreatedEvent
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }
}
```

### Subscribing to Events

#### From Another Plugin

```csharp
// Reference the contracts project
// <ProjectReference Include="../FluentCMS.Plugins.CRM.Contracts/..." />

using FluentCMS.Plugins.CRM.Contracts.Events;
using FluentCMS.Infrastructure.EventBus.Abstractions;

public class CustomerCreatedEventHandler : IEventSubscriber<CustomerCreatedEvent>
{
    private readonly ILogger<CustomerCreatedEventHandler> _logger;
    private readonly IAccountingService _accountingService;
    
    public CustomerCreatedEventHandler(
        ILogger<CustomerCreatedEventHandler> logger,
        IAccountingService accountingService)
    {
        _logger = logger;
        _accountingService = accountingService;
    }
    
    public async Task Handle(CustomerCreatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Customer created: {CustomerId}, creating account", 
            domainEvent.CustomerId);
        
        // React to the event
        await _accountingService.CreateAccountForCustomer(
            domainEvent.CustomerId, 
            cancellationToken);
    }
}
```

#### Register the Handler

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // MediatR automatically registers handlers
    services.AddScoped<IEventSubscriber<CustomerCreatedEvent>, CustomerCreatedEventHandler>();
}
```

### Creating Event Contracts

```csharp
// In YourPlugin.Contracts project
using FluentCMS.Infrastructure.EventBus.Abstractions;

namespace FluentCMS.Plugins.CRM.Contracts.Events;

public class CustomerCreatedEvent : IEvent
{
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public Guid EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
```

## Middleware Registration

### Registering Middleware

```csharp
[Plugin]
public class LoggingMiddlewareStartup : IPluginStartup
{
    // Must run early in pipeline
    public override int ConfigurePriority => 5;
    
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register middleware dependencies
        services.AddScoped<IRequestLogger, RequestLogger>();
    }
    
    public void Configure(IApplicationBuilder app, IServiceProvider provider)
    {
        // Register middleware
        app.UseMiddleware<RequestLoggingMiddleware>();
    }
}
```

### Custom Middleware Implementation

```csharp
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    
    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context, IRequestLogger requestLogger)
    {
        var startTime = DateTimeOffset.UtcNow;
        
        try
        {
            await _next(context);
        }
        finally
        {
            var duration = DateTimeOffset.UtcNow - startTime;
            
            await requestLogger.LogRequest(
                context.Request.Path,
                context.Request.Method,
                context.Response.StatusCode,
                duration);
        }
    }
}
```

### Priority Examples

```csharp
// Authentication - Very early
public override int ConfigurePriority => 10;

// Authorization - After authentication
public override int ConfigurePriority => 20;

// CORS - Early
public override int ConfigurePriority => 15;

// API Endpoints - Later
public override int ConfigurePriority => 100;

// Error Handling - Very early
public override int ConfigurePriority => 1;
```

## Health Checks

### Implementing Health Check

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

public class CRMHealthCheck : IHealthCheck
{
    private readonly CRMDbContext _dbContext;
    private readonly IExternalApiClient _apiClient;
    
    public CRMHealthCheck(CRMDbContext dbContext, IExternalApiClient apiClient)
    {
        _dbContext = dbContext;
        _apiClient = apiClient;
    }
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check database connectivity
            await _dbContext.Database.CanConnectAsync(cancellationToken);
            
            // Check external API
            var apiHealthy = await _apiClient.HealthCheck(cancellationToken);
            
            if (!apiHealthy)
            {
                return HealthCheckResult.Degraded(
                    "External API is not responding");
            }
            
            return HealthCheckResult.Healthy("CRM plugin is healthy");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "CRM plugin is unhealthy",
                ex);
        }
    }
}
```

### Registering Health Check

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddHealthChecks()
        .AddCheck<CRMHealthCheck>("crm_plugin");
}
```

## Plugin Governance

To ensure the stability and maintainability of the entire system, all plugins must adhere to a set of governance principles.

### Pull Request Checklist

Before merging any changes to a plugin, the pull request must satisfy the requirements outlined in the **[Plugin Pull Request Checklist](./PLUGIN_PULL_REQUEST_CHECKLIST.md)**. This checklist enforces best practices related to performance, resource management, security, and testing.

### Event Catalog

When creating or modifying events that will be consumed by other plugins, you must document them in the **[Event Catalog](./EVENT_CATALOG.md)**. This central registry is critical for managing the implicit dependencies that an event-driven architecture creates.

### Event Versioning

Events, once consumed by other plugins, are considered public contracts. You should not make breaking changes to an existing event. Instead, create a new version of the event (e.g., `CustomerUpdatedEventV2`) and deprecate the old one.

## Observability & Debugging

A modular system requires robust observability to be debuggable. All plugins are expected to integrate with the system's tracing and logging standards.

### Structured Logging

Use structured logging for all log output. This allows for easier filtering and querying in log aggregation systems.

```csharp
_logger.LogInformation(
    "Processing customer {CustomerId} in plugin {PluginName}",
    customerId,
    "CRM Plugin");
```

### Communication Tracing

The system supports communication tracing to track a logical operation as it crosses plugin boundaries via events. This is accomplished via a `TraceContext` that is automatically propagated with events.

When logging within an event handler, always include the correlation data from the event's `TraceInfo` property:

```csharp
public async Task Handle(CustomerCreatedEvent domainEvent, CancellationToken cancellationToken = default)
{
    // It's recommended to start the log scope with the tracing info
    using (_logger.BeginScope("CorrelationId: {CorrelationId}", domainEvent.TraceInfo?.CorrelationId))
    {
        _logger.LogInformation(
            "Handling CustomerCreatedEvent for Customer {CustomerId}",
            domainEvent.CustomerId);
        
        // ... handler logic ...
    }
}
```

This ensures that the entire lifecycle of a business process can be traced in your logging system using a single `CorrelationId`.

## Best Practices

### 1. Single Responsibility

Each plugin should have one clear purpose:

✅ **Good**: Separate plugins for CRM, Accounting, Inventory
❌ **Bad**: One plugin handling CRM + Accounting + Inventory

### 2. Use Event Contracts

Share events via separate contracts projects:

```
FluentCMS.Plugins.CRM.Contracts/    # Share this
└── Events/
    └── CustomerCreatedEvent.cs

FluentCMS.Plugins.Accounting/       # Reference contracts
└── EventHandlers/
    └── CustomerCreatedEventHandler.cs
```

### 3. Configuration Isolation

Use plugin-specific configuration sections:

```json
{
  "Plugins": {
    "PluginA": { /* ... */ },
    "PluginB": { /* ... */ }
  }
}
```

### 4. Structured Logging

Include plugin context in logs:

```csharp
_logger.LogInformation(
    "Processing customer {CustomerId} in {PluginName}",
    customerId,
    "CRM Plugin");
```

### 5. Async All the Way

Use async/await consistently:

```csharp
// Good
public async Task<Customer> GetCustomer(int id, CancellationToken cancellationToken = default)
{
    return await _dbContext.Customers.FindAsync(new object[] { id }, cancellationToken);
}

// Bad
public Customer GetCustomer(int id)
{
    return _dbContext.Customers.Find(id);
}
```

### 6. Cancellation Token Support

Always accept and pass cancellation tokens:

```csharp
public async Task CreateCustomer(
    CreateCustomerDto dto,
    CancellationToken cancellationToken = default)
{
    var customer = new Customer { /* ... */ };
    await _dbContext.SaveChangesAsync(cancellationToken);
    await _eventPublisher.Publish(event, cancellationToken);
}
```

### 7. Fail Gracefully

Handle errors properly:

```csharp
public async Task<Result<Customer>> GetCustomer(int id, CancellationToken cancellationToken = default)
{
    try
    {
        var customer = await _dbContext.Customers.FindAsync(
            new object[] { id }, 
            cancellationToken);
        
        return customer == null 
            ? Result<Customer>.NotFound() 
            : Result<Customer>.Success(customer);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error retrieving customer {CustomerId}", id);
        return Result<Customer>.Error("Failed to retrieve customer");
    }
}
```

## Common Patterns

### Repository Pattern

```csharp
public interface ICustomerRepository
{
    Task<Customer?> GetById(int id, CancellationToken cancellationToken = default);
    Task<List<Customer>> GetAll(CancellationToken cancellationToken = default);
    Task Add(Customer customer, CancellationToken cancellationToken = default);
    Task Update(Customer customer, CancellationToken cancellationToken = default);
    Task Delete(int id, CancellationToken cancellationToken = default);
}

public class CustomerRepository : ICustomerRepository
{
    private readonly CRMDbContext _context;
    
    public CustomerRepository(CRMDbContext context)
    {
        _context = context;
    }
    
    public async Task<Customer?> GetById(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }
    
    // Other implementations...
}
```

### Unit of Work Pattern

```csharp
public interface IUnitOfWork : IDisposable
{
    ICustomerRepository Customers { get; }
    IOrderRepository Orders { get; }
    Task<int> SaveChanges(CancellationToken cancellationToken = default);
}

public class UnitOfWork : IUnitOfWork
{
    private readonly CRMDbContext _context;
    
    public UnitOfWork(CRMDbContext context)
    {
        _context = context;
        Customers = new CustomerRepository(context);
        Orders = new OrderRepository(context);
    }
    
    public ICustomerRepository Customers { get; }
    public IOrderRepository Orders { get; }
    
    public async Task<int> SaveChanges(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
    
    public void Dispose()
    {
        _context.Dispose();
    }
}
```

### Specification Pattern

```csharp
public interface ISpecification<T>
{
    Expression<Func<T, bool>> Criteria { get; }
}

public class ActiveCustomersSpec : ISpecification<Customer>
{
    public Expression<Func<Customer, bool>> Criteria => c => c.IsActive;
}

// Usage
var spec = new ActiveCustomersSpec();
var customers = await _context.Customers
    .Where(spec.Criteria)
    .ToListAsync(cancellationToken);
```

## Troubleshooting

### Plugin Not Loading

**Symptoms**: Plugin not discovered

**Solutions**:
1. Ensure `[Plugin]` attribute is present
2. Verify assembly name matches scan patterns
3. Check plugin implements `IPluginStartup`
4. Review logs for validation errors

### Dependency Errors

**Symptoms**: Missing dependency exception

**Solutions**:
1. Add project reference to dependent plugin
2. Ensure dependent plugin has `[Plugin]` attribute
3. Check for circular dependencies
4. Verify build order in solution

### Service Not Resolved

**Symptoms**: DI cannot resolve service

**Solutions**:
1. Verify service registered in `ConfigureServices()`
2. Check service lifetime (scoped vs singleton)
3. Ensure dependencies are registered
4. Check for interface vs implementation mismatch

### Configuration Not Loading

**Symptoms**: Settings have default values

**Solutions**:
1. Verify the key in `appsettings.json` under the `Plugins` section matches your plugin's assembly name (e.g., `"FluentCMS.Plugins.MyPlugin"`) or the overridden `Name` in your startup class.
2. Ensure you are binding directly to the `IConfiguration` object passed to `ConfigureServices` (e.g., `services.Configure<Settings>(configuration)`).
3. Ensure the host's `appsettings.json` is correctly formatted and copied to the output directory.
4. Validate JSON syntax.

### Event Not Received

**Symptoms**: Event handler not called

**Solutions**:
1. Verify handler registered in DI
2. Check event type matches exactly
3. Ensure MediatR pipeline configured
4. Review event publishing code

---

**Next**: [API Reference](./API-REFERENCE.md)
