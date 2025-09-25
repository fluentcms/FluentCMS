# FluentCMS.Providers

The core provider system for FluentCMS that enables pluggable functionality through a modular architecture. This system allows different implementations of services to be registered, discovered, and resolved at runtime.

## Overview

The provider system is designed to support multiple implementations of the same functionality (e.g., different email providers, file storage providers) while ensuring only one provider per functional area is active at any time.

## Key Features

- **Modular Architecture**: Plugin-based system supporting hot-swappable providers
- **Thread-Safe Operations**: Concurrent access with proper synchronization
- **Automatic Discovery**: Assembly scanning for provider modules
- **Configuration Support**: Both database and configuration file-based provider management
- **Dependency Injection**: Full ASP.NET Core DI integration
- **Performance Optimized**: Caching, lazy loading, and optimized reflection operations

## Core Components

### ProviderManager
The central orchestrator that manages provider lifecycle and resolution.

```csharp
public interface IProviderManager
{
    Task<IProviderModule?> GetProviderModule(string area, string typeName, CancellationToken cancellationToken = default!);
    Task<ProviderCatalog?> GetActiveByArea(string area, CancellationToken cancellationToken = default);
    Task RefreshProviders(CancellationToken cancellationToken = default);
}
```

### ProviderCatalogCache
Thread-safe cache for provider catalogs with immutable collections.

```csharp
// Get active provider for an area
var catalog = providerCatalogCache.GetActiveCatalog("Email");

// Get specific provider
var provider = providerCatalogCache.GetCatalog("Email", "SmtpProvider");
```

### ProviderDiscovery
Discovers provider modules from assemblies with performance optimizations.

```csharp
var discovery = new ProviderDiscovery(options);
var modules = discovery.GetProviderModules();
```

## Setup and Configuration

### 1. Basic Setup

```csharp
// In Program.cs or Startup.cs
services.AddProviders(options =>
{
    options.AssemblyPrefixesToScan.Add("FluentCMS");
    options.AssemblyPrefixesToScan.Add("MyCompany.Providers");
    options.EnableLogging = true;
    options.IgnoreExceptions = false;
});
```

### 2. Using Configuration Files

```csharp
services.AddProviders()
       .UseConfiguration();
```

**appsettings.json:**
```json
{
  "Providers": {
    "Email": {
      "SmtpProvider": {
        "Name": "SmtpProvider",
        "Active": true,
        "Module": "MyCompany.Email.SmtpProviderModule",
        "Options": {
          "SmtpServer": "smtp.gmail.com",
          "Port": 587,
          "UseSsl": true
        }
      }
    },
    "Storage": {
      "LocalFileProvider": {
        "Name": "LocalFileProvider", 
        "Active": true,
        "Module": "MyCompany.Storage.LocalFileProviderModule",
        "Options": {
          "BasePath": "/var/storage"
        }
      }
    }
  }
}
```

### 3. Using Entity Framework

```csharp
services.AddProviders()
       .UseEntityFramework();
       
// DbContext registration
services.AddDbContext<ProviderDbContext>(options =>
    options.UseSqlServer(connectionString));
```

## Creating Custom Providers

### 1. Define Your Interface

```csharp
public interface IEmailProvider : IProvider
{
    Task SendEmailAsync(string to, string subject, string body);
}
```

### 2. Create Provider Implementation

```csharp
public class SmtpProvider : IEmailProvider
{
    private readonly SmtpOptions _options;
    
    public SmtpProvider(SmtpOptions options)
    {
        _options = options;
    }
    
    public async Task SendEmailAsync(string to, string subject, string body)
    {
        // Implementation here
    }
}
```

### 3. Create Options Class

```csharp
public class SmtpOptions
{
    public string SmtpServer { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
```

### 4. Create Provider Module

```csharp
public class SmtpProviderModule : ProviderModuleBase<SmtpProvider, SmtpOptions>
{
    public override string Area => "Email";
    public override string DisplayName => "SMTP Email Provider";
    
    public override void ConfigureServices(IServiceCollection services)
    {
        // Register additional services if needed
        services.AddTransient<ISmtpClient, SmtpClient>();
    }
}
```

## Usage Examples

### Resolving Providers

```csharp
public class EmailService
{
    private readonly IEmailProvider _emailProvider;
    
    public EmailService(IEmailProvider emailProvider)
    {
        _emailProvider = emailProvider;
    }
    
    public async Task SendWelcomeEmail(string userEmail)
    {
        await _emailProvider.SendEmailAsync(
            userEmail, 
            "Welcome!", 
            "Thanks for joining us!");
    }
}
```

### Managing Providers Programmatically

```csharp
public class ProviderManagementService
{
    private readonly IProviderManager _providerManager;
    private readonly IProviderRepository _repository;
    
    public ProviderManagementService(
        IProviderManager providerManager,
        IProviderRepository repository)
    {
        _providerManager = providerManager;
        _repository = repository;
    }
    
    public async Task SwitchEmailProvider(string newProviderName)
    {
        // Deactivate current provider
        var currentProvider = await _repository.GetByArea("Email");
        foreach (var provider in currentProvider.Where(p => p.IsActive))
        {
            provider.IsActive = false;
            await _repository.Update(provider);
        }
        
        // Activate new provider
        var newProvider = await _repository.GetByAreaAndName("Email", newProviderName);
        if (newProvider != null)
        {
            newProvider.IsActive = true;
            await _repository.Update(newProvider);
        }
        
        // Refresh cache
        await _providerManager.RefreshProviders();
    }
}
```

## Performance Considerations

### Caching
- Provider modules are cached after discovery
- Assembly reflection results are cached
- JSON deserialization results are cached

### Thread Safety
- All operations are thread-safe
- Uses `ConcurrentDictionary` and `ImmutableList` collections
- Proper locking for initialization operations

### Memory Management
- Assembly loading uses `AssemblyLoadContext` for better cleanup
- Disposal patterns implemented where needed
- Cache clearing methods available for memory pressure scenarios

## Error Handling

### Common Exceptions

- `InvalidOperationException`: Provider configuration errors
- `ArgumentException`: Invalid parameters
- `JsonException`: Malformed provider options

### Error Recovery

```csharp
try
{
    var provider = serviceProvider.GetRequiredService<IEmailProvider>();
    await provider.SendEmailAsync(to, subject, body);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("No active provider"))
{
    // Handle missing provider scenario
    _logger.LogError(ex, "No email provider configured");
    // Fallback logic here
}
```

## Troubleshooting

### Provider Not Found
1. Ensure assembly prefix is included in `AssemblyPrefixesToScan`
2. Verify provider module has parameterless constructor
3. Check module implements `IProviderModule` correctly

### Multiple Active Providers Error
1. Only one provider per area can be active
2. Check configuration for duplicate active entries
3. Verify database constraints are enforced

### Assembly Loading Issues
1. Ensure all dependencies are available
2. Check assembly compatibility (.NET version)
3. Review `IgnoreExceptions` setting for debugging

## Best Practices

1. **Provider Design**: Keep providers stateless and thread-safe
2. **Options Pattern**: Use strongly-typed options classes
3. **Dependency Injection**: Register provider dependencies in `ConfigureServices`
4. **Error Handling**: Implement proper error handling and logging
5. **Testing**: Create mock implementations for unit testing
6. **Documentation**: Document provider configuration requirements

## Dependencies

- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Configuration
- Microsoft.Extensions.Options
- System.Text.Json
- System.Collections.Immutable

## API Reference

See the [FluentCMS.Providers.Abstractions](../FluentCMS.Providers.Abstractions/README.md) project for detailed interface documentation.
