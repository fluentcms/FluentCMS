# FluentCMS Configuration

A robust, database-backed configuration provider for ASP.NET Core applications that stores configuration data in SQL databases with automatic seeding and real-time reloading capabilities.

## Features

- 🗄️ **Database-Backed Configuration**: Store configuration in SQLite or SQL Server databases
- 🔄 **Automatic Seeding**: Seed default configuration values from appsettings.json at startup
- ⚡ **Real-Time Reloading**: Optional automatic configuration refresh with configurable intervals
- 🛡️ **Security First**: Built-in input validation, SQL injection prevention, and secure parameter handling
- 📦 **Strongly Typed**: Full support for strongly-typed configuration classes with data annotations
- 🔧 **Easy Integration**: Simple extension methods for seamless ASP.NET Core integration

## Supported Databases

- **SQLite** - Perfect for development and lightweight applications
- **SQL Server** - Enterprise-ready for production environments

## Installation

Install the core package and your preferred database provider:

```bash
# Core package (required)
dotnet add package FluentCMS.Configuration

# Database providers (choose one or both)
dotnet add package FluentCMS.Configuration.Sqlite
dotnet add package FluentCMS.Configuration.SqlServer
```

## Quick Start

### 1. Define Your Configuration Classes

```csharp
public class EmailSettings
{
    public string SmtpServer { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class AppSettings
{
    public string ApplicationName { get; set; } = "FluentCMS";
    public string Version { get; set; } = "1.0.0";
    public bool MaintenanceMode { get; set; } = false;
    public List<string> AllowedHosts { get; set; } = new() { "*" };
}
```

### 2. Configure Your appsettings.json

```json
{
  "EmailSettings": {
    "SmtpServer": "smtp.gmail.com",
    "Port": 587,
    "EnableSsl": true,
    "Username": "your-email@gmail.com",
    "Password": "your-app-password"
  },
  "AppSettings": {
    "ApplicationName": "My FluentCMS App",
    "Version": "1.0.0",
    "MaintenanceMode": false,
    "AllowedHosts": ["localhost", "*.mydomain.com"]
  }
}
```

### 3. Setup Database Configuration

#### SQLite Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add SQLite database configuration
builder.AddSqliteOptions("Data Source=config.db", reloadInterval: 30);

// Register your configuration classes
builder.Services.AddDbOptions<EmailSettings>(builder.Configuration, "EmailSettings");
builder.Services.AddDbOptions<AppSettings>(builder.Configuration, "AppSettings");

var app = builder.Build();
```

#### SQL Server Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add SQL Server database configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.AddSqlServerOptions(connectionString, reloadInterval: 60);

// Register your configuration classes
builder.Services.AddDbOptions<EmailSettings>(builder.Configuration, "EmailSettings");
builder.Services.AddDbOptions<AppSettings>(builder.Configuration, "AppSettings");

var app = builder.Build();
```

### 4. Use Configuration in Your Services

```csharp
[ApiController]
[Route("api/[controller]")]
public class EmailController : ControllerBase
{
    private readonly EmailSettings _emailSettings;
    private readonly IOptionsMonitor<AppSettings> _appSettings;

    public EmailController(
        IOptions<EmailSettings> emailSettings,
        IOptionsMonitor<AppSettings> appSettings)
    {
        _emailSettings = emailSettings.Value;
        _appSettings = appSettings;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendEmail([FromBody] EmailRequest request)
    {
        // Access current configuration values
        var currentAppSettings = _appSettings.CurrentValue;
        
        if (currentAppSettings.MaintenanceMode)
        {
            return ServiceUnavailable("Application is in maintenance mode");
        }

        // Use email settings
        using var client = new SmtpClient(_emailSettings.SmtpServer, _emailSettings.Port)
        {
            EnableSsl = _emailSettings.EnableSsl,
            Credentials = new NetworkCredential(_emailSettings.Username, _emailSettings.Password)
        };

        // Send email logic here...
        return Ok("Email sent successfully");
    }
}
```

## Advanced Configuration

### Data Annotations Validation

Add validation attributes to your configuration classes:

```csharp
public class EmailSettings
{
    [Required]
    [EmailAddress]
    public string Username { get; set; } = "";

    [Required]
    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    [Required]
    public string SmtpServer { get; set; } = "localhost";
}
```

### Configuration Sections

Organize related settings into nested sections:

```csharp
public class DatabaseSettings
{
    public ConnectionStrings ConnectionStrings { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
}

public class ConnectionStrings
{
    public string DefaultConnection { get; set; } = "";
    public string ReadOnlyConnection { get; set; } = "";
}

public class PerformanceSettings
{
    public int CommandTimeout { get; set; } = 30;
    public int MaxRetryCount { get; set; } = 3;
    public bool EnableConnectionPooling { get; set; } = true;
}
```

### Real-Time Configuration Updates

Monitor configuration changes in real-time:

```csharp
public class ConfigurationService : IHostedService
{
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private readonly ILogger<ConfigurationService> _logger;
    private IDisposable? _changeSubscription;

    public ConfigurationService(
        IOptionsMonitor<AppSettings> appSettings,
        ILogger<ConfigurationService> logger)
    {
        _appSettings = appSettings;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe to configuration changes
        _changeSubscription = _appSettings.OnChange((settings, name) =>
        {
            _logger.LogInformation("Configuration changed: {SettingsName}", name);
            
            if (settings.MaintenanceMode)
            {
                _logger.LogWarning("Application entered maintenance mode");
                // Handle maintenance mode logic
            }
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _changeSubscription?.Dispose();
        return Task.CompletedTask;
    }
}
```

## Security Features

### Input Validation

The system automatically validates all configuration data:

- **Section Names**: Limited to 450 characters, no control characters
- **JSON Content**: Validated for proper JSON format, 1MB size limit
- **Connection Strings**: Validated against dangerous keywords and patterns
- **Reload Intervals**: Bounded between 5 seconds and 24 hours

### SQL Injection Prevention

All database operations use parameterized queries with explicit type checking:

```csharp
// SQLite - Uses parameterized queries
private const string UpsertSql = @"INSERT INTO Options (Section, Value)
        VALUES ($section, $value)
        ON CONFLICT(Section) DO NOTHING;";

// SQL Server - Uses strongly-typed parameters
var pSection = new SqlParameter("@section", SqlDbType.NVarChar, 450) { Value = section };
var pValue = new SqlParameter("@value", SqlDbType.NVarChar, -1) { Value = jsonValue };
```

## Configuration Options

### Reload Intervals

Configure how often the system checks for configuration changes:

```csharp
// Check every 30 seconds
builder.AddSqliteOptions("Data Source=config.db", reloadInterval: 30);

// Check every 5 minutes
builder.AddSqlServerOptions(connectionString, reloadInterval: 300);

// No automatic reloading (manual only)
builder.AddSqliteOptions("Data Source=config.db");
```

### Seeding Control

Control whether default values are seeded from appsettings.json:

```csharp
// Seed default values (default behavior)
builder.Services.AddDbOptions<EmailSettings>(builder.Configuration, "EmailSettings", seedData: true);

// Skip seeding (use only database values)
builder.Services.AddDbOptions<EmailSettings>(builder.Configuration, "EmailSettings", seedData: false);
```

## Database Schema

The system creates a simple table structure:

### SQLite Schema
```sql
CREATE TABLE IF NOT EXISTS Options (
    Section TEXT PRIMARY KEY,
    Value   TEXT NOT NULL
);
```

### SQL Server Schema
```sql
CREATE TABLE dbo.Options (
    Section NVARCHAR(450) NOT NULL PRIMARY KEY,
    Value   NVARCHAR(MAX) NOT NULL
);
```

## Manual Configuration Updates

You can programmatically trigger configuration reloads:

```csharp
public class ConfigurationController : ControllerBase
{
    private readonly DbConfigurationSource _configSource;

    public ConfigurationController(DbConfigurationSource configSource)
    {
        _configSource = configSource;
    }

    [HttpPost("reload")]
    public IActionResult ReloadConfiguration()
    {
        // Manually trigger configuration reload
        _configSource.Provider.TriggerReload();
        return Ok("Configuration reloaded");
    }
}
```

## Best Practices

### 1. Use IOptionsMonitor for Dynamic Settings
```csharp
// ✅ Good - Supports real-time updates
private readonly IOptionsMonitor<AppSettings> _settings;

// ❌ Avoid - Static snapshot at startup
private readonly IOptions<AppSettings> _settings;
```

### 2. Validate Configuration at Startup
```csharp
builder.Services.AddDbOptions<EmailSettings>(builder.Configuration, "EmailSettings")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

### 3. Use Appropriate Reload Intervals
```csharp
// ✅ Good for production - reasonable interval
builder.AddSqlServerOptions(connectionString, reloadInterval: 300); // 5 minutes

// ❌ Avoid - too frequent, may impact performance
builder.AddSqlServerOptions(connectionString, reloadInterval: 1); // 1 second
```

### 4. Handle Configuration Errors Gracefully
```csharp
public void ConfigureServices(IServiceCollection services)
{
    try
    {
        services.AddDbOptions<EmailSettings>(Configuration, "EmailSettings");
    }
    catch (ArgumentException ex)
    {
        // Log configuration validation errors
        logger.LogError(ex, "Invalid configuration for EmailSettings");
        throw;
    }
}
```

## Troubleshooting

### Common Issues

**Configuration not updating:**
- Check that reload interval is set
- Verify database connectivity
- Ensure the configuration section exists in the database

**Validation errors:**
- Check that JSON in database is valid
- Verify section names don't contain invalid characters
- Ensure configuration classes have parameterless constructors

**Performance issues:**
- Increase reload interval for production
- Monitor database connection pool usage
- Consider using read-only database connections for configuration reads

### Logging

Enable detailed logging to troubleshoot issues:

```csharp
builder.Logging.AddFilter("FluentCMS.Configuration", LogLevel.Debug);
```

## Contributing

Contributions are welcome! Please read our contributing guidelines and submit pull requests to our GitHub repository.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
