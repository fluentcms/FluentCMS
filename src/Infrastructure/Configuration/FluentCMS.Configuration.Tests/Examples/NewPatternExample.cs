using FluentCMS.Configuration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Configuration.Tests.Examples;

/// <summary>
/// Example demonstrating the new AddDatabaseOptions pattern (recommended approach)
/// </summary>
public class NewPatternExample
{
    public void ConfigureApplication()
    {
        var services = new ServiceCollection();

        // 1. Setup basic configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // 2. Library developers register their configuration sections
        // This would typically be in AddMyLibrary() extension methods
        ConfigureEmailLibrary(services, configuration);
        ConfigureFeatureFlagsLibrary(services, configuration);
        ConfigureApiLibrary(services, configuration);

        // 3. Enable database configuration (auto-discovers registered sections)
        var configBuilder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddDatabaseConfiguration(
                "Data Source=config.db",
                reloadInterval: TimeSpan.FromMinutes(5)
            );

        var finalConfig = configBuilder.Build();
        services.AddSingleton<IConfiguration>(finalConfig);

        var serviceProvider = services.BuildServiceProvider();
    }

    // Example: Email library registration
    private static void ConfigureEmailLibrary(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IEmailService, EmailService>();
    }

    // Example: Feature flags library registration
    private static void ConfigureFeatureFlagsLibrary(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseOptions<FeatureFlags>("FeatureFlags", configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IFeatureFlagService, FeatureFlagService>();
    }

    // Example: API library registration
    private static void ConfigureApiLibrary(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseOptions<ApiSettings>("ApiSettings", configuration)
            .Validate(settings =>
            {
                // Custom validation
                return !string.IsNullOrEmpty(settings.BaseUrl) && settings.TimeoutSeconds > 0;
            }, "API settings are invalid")
            .ValidateOnStart();

        services.AddScoped<IApiClient, ApiClient>();
    }

    // Example: Named options pattern
    public void ConfigureNamedOptions(IServiceCollection services, IConfiguration configuration)
    {
        // Register multiple email configurations
        services.AddDatabaseOptions<EmailSettings>("EmailSettings:Primary", configuration)
            .ValidateOnStart();

        services.AddDatabaseOptions<EmailSettings>("EmailSettings:Secondary", configuration)
            .ValidateOnStart();

        // Usage in service
        services.AddScoped<IEmailService>(sp =>
        {
            var monitor = sp.GetRequiredService<IOptionsMonitor<EmailSettings>>();
            return new EmailService(monitor);
        });
    }

    // Example: Post-configuration
    public void ConfigureWithPostConfiguration(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", configuration)
            .ValidateDataAnnotations()
            .PostConfigure(settings =>
            {
                // Modify settings after binding
                if (string.IsNullOrEmpty(settings.FromName))
                {
                    settings.FromName = "FluentCMS";
                }
            })
            .ValidateOnStart();
    }
}

// Example: Library extension method pattern
public static class EmailLibraryExtensions
{
    /// <summary>
    /// Adds email services with database-backed configuration
    /// </summary>
    public static IServiceCollection AddEmailLibrary(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration for database storage with validation
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", configuration)
            .ValidateDataAnnotations()
            .Validate(settings =>
            {
                return settings.SmtpPort > 0 && settings.SmtpPort <= 65535;
            }, "SMTP port must be between 1 and 65535")
            .ValidateOnStart();

        // Register library services
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IEmailTemplateService, EmailTemplateService>();

        return services;
    }
}

public static class FeatureFlagsLibraryExtensions
{
    /// <summary>
    /// Adds feature flags with database-backed configuration
    /// </summary>
    public static IServiceCollection AddFeatureFlags(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration for database storage
        services.AddDatabaseOptions<FeatureFlags>("FeatureFlags", configuration)
            .ValidateDataAnnotations()
            .PostConfigure(flags =>
            {
                // Set defaults for new features
                if (flags.MaxUploadSizeMB <= 0)
                {
                    flags.MaxUploadSizeMB = 10;
                }
            })
            .ValidateOnStart();

        // Register services
        services.AddScoped<IFeatureFlagService, FeatureFlagService>();

        return services;
    }
}

// Example configuration classes with validation
public class EmailSettings
{
    [Required(ErrorMessage = "SMTP Host is required")]
    [MinLength(3)]
    public string SmtpHost { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535")]
    public int SmtpPort { get; set; }

    public bool EnableSsl { get; set; }

    [Required]
    [EmailAddress]
    public string FromEmail { get; set; } = string.Empty;

    public string? FromName { get; set; }
}

public class FeatureFlags
{
    public bool EnableNewUI { get; set; }
    public bool EnableBetaFeatures { get; set; }
    public bool EnableAdvancedSearch { get; set; }

    [Range(1, 1000)]
    public int MaxUploadSizeMB { get; set; } = 10;
}

public class ApiSettings
{
    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    [Range(0, 10)]
    public int RetryCount { get; set; } = 3;

    public string? ApiKey { get; set; }
}

// Example service implementations
public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
}

public class EmailService : IEmailService
{
    private EmailSettings _settings;

    public EmailService(IOptionsMonitor<EmailSettings> monitor)
    {
        _settings = monitor.CurrentValue;

        // React to configuration changes at runtime
        monitor.OnChange(settings =>
        {
            _settings = settings;
            Console.WriteLine($"Email settings updated: SMTP Host = {settings.SmtpHost}");
        });
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        Console.WriteLine($"Sending email via {_settings.SmtpHost}:{_settings.SmtpPort}");
        await Task.CompletedTask;
    }
}

public interface IEmailTemplateService
{
    Task<string> RenderTemplateAsync(string templateName, object model);
}

public class EmailTemplateService : IEmailTemplateService
{
    public Task<string> RenderTemplateAsync(string templateName, object model)
    {
        return Task.FromResult($"<html>Template: {templateName}</html>");
    }
}

public interface IFeatureFlagService
{
    bool IsEnabled(string featureName);
}

public class FeatureFlagService : IFeatureFlagService
{
    private readonly FeatureFlags _flags;

    public FeatureFlagService(IOptions<FeatureFlags> options)
    {
        _flags = options.Value;
    }

    public bool IsEnabled(string featureName)
    {
        return featureName switch
        {
            "NewUI" => _flags.EnableNewUI,
            "BetaFeatures" => _flags.EnableBetaFeatures,
            "AdvancedSearch" => _flags.EnableAdvancedSearch,
            _ => false
        };
    }
}

public interface IApiClient
{
    Task<T> GetAsync<T>(string endpoint);
}

public class ApiClient : IApiClient
{
    private readonly ApiSettings _settings;

    public ApiClient(IOptions<ApiSettings> options)
    {
        _settings = options.Value;
    }

    public async Task<T> GetAsync<T>(string endpoint)
    {
        Console.WriteLine($"Calling {_settings.BaseUrl}{endpoint}");
        await Task.CompletedTask;
        return default!;
    }
}
