# FluentCMS.Providers.Abstractions

The foundational abstractions and contracts for the FluentCMS provider system. This package contains the core interfaces, base classes, and contracts that all provider implementations must follow.

## Overview

This package defines the core abstractions that enable the pluggable provider architecture in FluentCMS. It provides the necessary interfaces and base classes for creating provider modules and implementing provider patterns.

## Key Components

### Core Interfaces

#### IProvider
The base marker interface that all provider implementations must implement.

```csharp
/// <summary>
/// Base marker interface for all providers.
/// All provider implementations must implement this interface.
/// </summary>
public interface IProvider
{
}
```

**Usage:**
```csharp
public interface IEmailProvider : IProvider
{
    Task SendEmailAsync(string to, string subject, string body);
}

public class SmtpProvider : IEmailProvider
{
    // Implementation
}
```

#### IProviderModule
Defines the contract for provider modules that register and configure providers.

```csharp
public interface IProviderModule
{
    string Area { get; }
    string DisplayName { get; }
    Type ProviderType { get; }
    Type? OptionsType { get; }
    Type InterfaceType { get; }
    void ConfigureServices(IServiceCollection services);
}
```

**Properties:**
- `Area`: The functional area this provider belongs to (e.g., "Email", "Storage")
- `DisplayName`: Human-readable name for administrative purposes
- `ProviderType`: The concrete provider implementation type
- `OptionsType`: Optional configuration options type
- `InterfaceType`: The service interface the provider implements
- `ConfigureServices`: Method to register additional services

#### Generic Provider Module Interfaces

```csharp
// For providers with options
public interface IProviderModule<TProvider, TOptions> : IProviderModule
    where TProvider : class, IProvider
    where TOptions : class, new()
{
}

// For providers without options
public interface IProviderModule<TProvider> : IProviderModule
    where TProvider : class, IProvider
{
}
```

### Base Classes

#### ProviderModuleBase<TProvider, TOptions>
Base class for provider modules with configuration options.

```csharp
public abstract class ProviderModuleBase<TProvider, TOptions> : ProviderModuleBase<TProvider>
    where TProvider : class, IProvider
    where TOptions : class, new()
{
    public override Type? OptionsType => typeof(TOptions);
}
```

**Example Implementation:**
```csharp
public class SmtpProviderModule : ProviderModuleBase<SmtpProvider, SmtpOptions>
{
    public override string Area => "Email";
    public override string DisplayName => "SMTP Email Provider";
    
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<ISmtpClient, SmtpClient>();
    }
}
```

#### ProviderModuleBase<TProvider>
Base class for provider modules without configuration options.

```csharp
public abstract class ProviderModuleBase<TProvider> : IProviderModule<TProvider>
    where TProvider : class, IProvider
{
    public abstract string Area { get; }
    public abstract string DisplayName { get; }
    public Type ProviderType => typeof(TProvider);
    public virtual Type? OptionsType => null;
    
    public virtual Type InterfaceType
    {
        get
        {
            var interfaces = typeof(TProvider).GetInterfaces()
                .Where(i => i != typeof(IProvider) && typeof(IProvider).IsAssignableFrom(i))
                .ToArray();

            if (interfaces.Length == 0)
                throw new InvalidOperationException($"Provider {typeof(TProvider).Name} must implement at least one interface that extends IProvider.");

            return interfaces.First();
        }
    }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        // Default implementation does nothing
    }
}
```

**Example Implementation:**
```csharp
public class ConsoleLoggerModule : ProviderModuleBase<ConsoleLogger>
{
    public override string Area => "Logging";
    public override string DisplayName => "Console Logger";
}
```

## Creating Provider Implementations

### Step 1: Define the Provider Interface

```csharp
public interface IFileStorageProvider : IProvider
{
    Task<string> SaveFileAsync(Stream fileStream, string fileName);
    Task<Stream> GetFileAsync(string fileId);
    Task DeleteFileAsync(string fileId);
    Task<bool> FileExistsAsync(string fileId);
}
```

### Step 2: Create Configuration Options (Optional)

```csharp
public class LocalStorageOptions
{
    public string BasePath { get; set; } = "/var/storage";
    public long MaxFileSize { get; set; } = 10 * 1024 * 1024; // 10MB
    public string[] AllowedExtensions { get; set; } = { ".jpg", ".png", ".pdf" };
}
```

### Step 3: Implement the Provider

```csharp
public class LocalFileStorageProvider : IFileStorageProvider
{
    private readonly LocalStorageOptions _options;
    private readonly ILogger<LocalFileStorageProvider> _logger;

    public LocalFileStorageProvider(
        LocalStorageOptions options,
        ILogger<LocalFileStorageProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<string> SaveFileAsync(Stream fileStream, string fileName)
    {
        var fileId = Guid.NewGuid().ToString();
        var filePath = Path.Combine(_options.BasePath, fileId);
        
        using var fileOutput = File.Create(filePath);
        await fileStream.CopyToAsync(fileOutput);
        
        _logger.LogInformation("File {FileName} saved as {FileId}", fileName, fileId);
        return fileId;
    }

    public async Task<Stream> GetFileAsync(string fileId)
    {
        var filePath = Path.Combine(_options.BasePath, fileId);
        
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File {fileId} not found");
            
        return File.OpenRead(filePath);
    }

    public async Task DeleteFileAsync(string fileId)
    {
        var filePath = Path.Combine(_options.BasePath, fileId);
        
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("File {FileId} deleted", fileId);
        }
    }

    public async Task<bool> FileExistsAsync(string fileId)
    {
        var filePath = Path.Combine(_options.BasePath, fileId);
        return File.Exists(filePath);
    }
}
```

### Step 4: Create the Provider Module

```csharp
public class LocalFileStorageModule : ProviderModuleBase<LocalFileStorageProvider, LocalStorageOptions>
{
    public override string Area => "FileStorage";
    public override string DisplayName => "Local File Storage Provider";

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register any additional services needed by the provider
        services.AddTransient<IFileValidator, FileValidator>();
    }
}
```

## Advanced Patterns

### Multiple Interface Implementation

```csharp
public interface ICacheProvider : IProvider
{
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task<T?> GetAsync<T>(string key);
    Task RemoveAsync(string key);
}

public interface IDistributedCacheProvider : ICacheProvider
{
    Task InvalidateAsync(string pattern);
    Task<IEnumerable<string>> GetKeysAsync(string pattern);
}

// Provider implementing multiple interfaces
public class RedisProvider : IDistributedCacheProvider
{
    // Implementation
}

// Module specifying the primary interface
public class RedisProviderModule : ProviderModuleBase<RedisProvider, RedisOptions>
{
    public override string Area => "Cache";
    public override string DisplayName => "Redis Cache Provider";
    
    // Override to specify the primary interface
    public override Type InterfaceType => typeof(IDistributedCacheProvider);
}
```

### Provider with Complex Dependencies

```csharp
public class DatabaseEmailProvider : IEmailProvider
{
    private readonly EmailOptions _options;
    private readonly IDbContext _dbContext;
    private readonly ITemplateEngine _templateEngine;

    public DatabaseEmailProvider(
        EmailOptions options,
        IDbContext dbContext,
        ITemplateEngine templateEngine)
    {
        _options = options;
        _dbContext = dbContext;
        _templateEngine = templateEngine;
    }

    // Implementation
}

public class DatabaseEmailModule : ProviderModuleBase<DatabaseEmailProvider, EmailOptions>
{
    public override string Area => "Email";
    public override string DisplayName => "Database Email Provider";

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register dependencies
        services.AddScoped<ITemplateEngine, RazorTemplateEngine>();
        services.AddScoped<IEmailTemplate, DatabaseEmailTemplate>();
    }
}
```

### Provider with Validation

```csharp
public class ValidatedOptions
{
    [Required]
    public string ApiKey { get; set; } = string.Empty;
    
    [Range(1, 100)]
    public int MaxRetries { get; set; } = 3;
    
    [Url]
    public string BaseUrl { get; set; } = string.Empty;
}

public class ValidatedProviderModule : ProviderModuleBase<MyProvider, ValidatedOptions>
{
    public override string Area => "External";
    public override string DisplayName => "External API Provider";

    public override void ConfigureServices(IServiceCollection services)
    {
        // Add options validation
        services.AddOptions<ValidatedOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
    }
}
```

## Best Practices

### 1. Interface Design
- Keep interfaces focused and cohesive
- Use async patterns for I/O operations
- Include cancellation token support
- Follow SOLID principles

```csharp
public interface INotificationProvider : IProvider
{
    Task SendNotificationAsync(
        string recipient, 
        string message, 
        NotificationOptions? options = null,
        CancellationToken cancellationToken = default);
        
    Task<bool> CanSendToAsync(
        string recipient, 
        CancellationToken cancellationToken = default);
}
```

### 2. Provider Implementation
- Make providers stateless when possible
- Use dependency injection for external dependencies
- Implement proper error handling and logging
- Consider thread safety requirements

```csharp
public class EmailProvider : IEmailProvider
{
    private readonly EmailOptions _options;
    private readonly ILogger<EmailProvider> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public EmailProvider(
        EmailOptions options,
        ILogger<EmailProvider> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        try
        {
            // Implementation with proper error handling
            _logger.LogInformation("Sending email to {Recipient}", to);
            // ... send logic
            _logger.LogInformation("Email sent successfully to {Recipient}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}", to);
            throw;
        }
    }
}
```

### 3. Options Configuration
- Use data annotations for validation
- Provide sensible defaults
- Document configuration requirements

```csharp
public class EmailOptions
{
    /// <summary>
    /// SMTP server hostname or IP address
    /// </summary>
    [Required]
    public string SmtpServer { get; set; } = string.Empty;

    /// <summary>
    /// SMTP server port (default: 587 for TLS)
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    /// <summary>
    /// Enable SSL/TLS encryption
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// Authentication username
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Authentication password
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Default sender email address
    /// </summary>
    [EmailAddress]
    public string? DefaultSender { get; set; }

    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}
```

### 4. Module Configuration
- Keep area names consistent and descriptive
- Provide clear display names
- Register necessary dependencies

```csharp
public class EmailProviderModule : ProviderModuleBase<EmailProvider, EmailOptions>
{
    public override string Area => "Email";
    public override string DisplayName => "SMTP Email Provider";

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register HTTP client for API calls
        services.AddHttpClient<EmailProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Register additional dependencies
        services.AddTransient<IEmailValidator, EmailValidator>();
        services.AddTransient<IEmailTemplateRenderer, EmailTemplateRenderer>();
    }
}
```

## Package Dependencies

This package has minimal dependencies to ensure broad compatibility:

- Microsoft.Extensions.DependencyInjection.Abstractions

## Version Compatibility

- .NET 9.0+
- Compatible with ASP.NET Core dependency injection
- Supports all Microsoft.Extensions.* packages

## See Also

- [FluentCMS.Providers](../FluentCMS.Providers/README.md) - Core provider system implementation
- [FluentCMS.Providers.Repositories.EntityFramework](../FluentCMS.Providers.Repositories.EntityFramework/README.md) - Entity Framework provider repository
