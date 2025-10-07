using FluentCMS.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Configuration.Tests;

public class DatabaseConfigurationTests : IDisposable
{
    private readonly string _connectionString;
    private readonly DbContextOptions<ConfigurationDbContext> _dbOptions;

    public DatabaseConfigurationTests()
    {
        _connectionString = $"Data Source=test_{Guid.NewGuid()}.db";
        _dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        // Clear registry before each test
        DatabaseConfigurationRegistry.Clear();
    }

    [Fact]
    public void AddDatabaseOptions_ShouldRegisterSection()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                ["EmailSettings:SmtpPort"] = "587"
            })
            .Build();

        // Act
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", configuration);

        // Assert
        Assert.True(DatabaseConfigurationRegistry.IsRegistered("EmailSettings"));
        var registered = DatabaseConfigurationRegistry.GetRegisteredSections();
        Assert.Contains("EmailSettings", registered.Keys);
        Assert.Equal(typeof(EmailSettings), registered["EmailSettings"]);
    }

    [Fact]
    public void AddDatabaseOptions_ShouldReturnOptionsBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com"
            })
            .Build();

        // Act
        var optionsBuilder = services.AddDatabaseOptions<EmailSettings>("EmailSettings", configuration);

        // Assert
        Assert.NotNull(optionsBuilder);
        Assert.IsAssignableFrom<OptionsBuilder<EmailSettings>>(optionsBuilder);
    }

    [Fact]
    public void AddDatabaseOptions_ShouldSupportFluentConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["EmailSettings:FromEmail"] = "test@example.com"
            })
            .Build();

        // Act
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<EmailSettings>>();

        // Assert
        Assert.NotNull(options.Value);
        Assert.Equal("smtp.example.com", options.Value.SmtpHost);
        Assert.Equal(587, options.Value.SmtpPort);
    }

    [Fact]
    public void AddDatabaseConfiguration_ShouldAutoDiscoverRegisteredSections()
    {
        // Arrange
        var services = new ServiceCollection();

        // Register sections via AddDatabaseOptions
        services.AddDatabaseOptions<EmailSettings>("EmailSettings");
        services.AddDatabaseOptions<FeatureFlags>("FeatureFlags");

        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmailSettings:SmtpHost"] = "smtp.example.com",
            ["EmailSettings:SmtpPort"] = "587",
            ["FeatureFlags:EnableNewUI"] = "true"
        });

        // Act - Should auto-discover EmailSettings and FeatureFlags
        configBuilder.AddDatabaseConfiguration(_connectionString);
        var config = configBuilder.Build();

        // Assert - Sections should be in database
        using var context = new ConfigurationDbContext(_dbOptions);

        var emailSettings = context.Configurations.FirstOrDefault(c => c.Section == "EmailSettings");
        Assert.NotNull(emailSettings);
        Assert.Contains("smtp.example.com", emailSettings.Value);

        var featureFlags = context.Configurations.FirstOrDefault(c => c.Section == "FeatureFlags");
        Assert.NotNull(featureFlags);
        Assert.Contains("EnableNewUI", featureFlags.Value);
    }

    [Fact]
    public void MultipleLibraries_ShouldRegisterIndependently()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                ["FeatureFlags:EnableNewUI"] = "true",
                ["ApiSettings:BaseUrl"] = "https://api.example.com"
            })
            .Build();

        // Act - Simulate multiple libraries registering their options
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", configuration);
        services.AddDatabaseOptions<FeatureFlags>("FeatureFlags", configuration);
        services.AddDatabaseOptions<ApiSettings>("ApiSettings", configuration);

        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["FeatureFlags:EnableNewUI"] = "true",
                ["ApiSettings:BaseUrl"] = "https://api.example.com",
                ["ApiSettings:TimeoutSeconds"] = "30"
            })
            .AddDatabaseConfiguration(_connectionString);

        var config = configBuilder.Build();

        // Assert
        using var context = new ConfigurationDbContext(_dbOptions);
        var sections = context.Configurations.Select(c => c.Section).ToList();

        Assert.Contains("EmailSettings", sections);
        Assert.Contains("FeatureFlags", sections);
        Assert.Contains("ApiSettings", sections);
        Assert.Equal(3, sections.Count);
    }

    [Fact]
    public void AddDatabaseOptions_ShouldSupportValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "", // Invalid - required
                ["EmailSettings:SmtpPort"] = "99999" // Invalid - out of range
            })
            .Build();

        services.AddDatabaseOptions<EmailSettings>("EmailSettings", configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        Assert.Throws<OptionsValidationException>(() =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<EmailSettings>>();
            _ = options.Value;
        });
    }

    [Fact]
    public void AddDatabaseOptions_ShouldSupportPostConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                ["EmailSettings:SmtpPort"] = "587"
            })
            .Build();

        services.AddDatabaseOptions<EmailSettings>("EmailSettings", configuration)
            .PostConfigure(settings =>
            {
                if (string.IsNullOrEmpty(settings.FromName))
                {
                    settings.FromName = "DefaultName";
                }
            });

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<IOptions<EmailSettings>>();

        // Assert
        Assert.Equal("DefaultName", options.Value.FromName);
    }

    [Fact]
    public async Task AddDatabaseOptions_ShouldWorkWithRuntimeUpdates()
    {
        // Arrange
        var services = new ServiceCollection();

        // Register section first
        services.AddDatabaseOptions<EmailSettings>("EmailSettings");

        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.old.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["EmailSettings:FromEmail"] = "old@example.com"
            })
            .AddDatabaseConfiguration(_connectionString);

        var finalConfig = configBuilder.Build();

        // Configure IOptions with the final config
        services.AddSingleton<IConfiguration>(finalConfig);
        services.Configure<EmailSettings>(finalConfig.GetSection("EmailSettings"));

        var serviceProvider = services.BuildServiceProvider();
        var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<EmailSettings>>();

        var initialValue = monitor.CurrentValue;
        Assert.Equal("smtp.old.com", initialValue.SmtpHost);

        // Act - Update configuration at runtime
        var provider = ((IConfigurationRoot)finalConfig).Providers
            .OfType<DatabaseConfigurationProvider>()
            .First();

        var newSettings = new EmailSettings
        {
            SmtpHost = "smtp.new.com",
            SmtpPort = 465,
            EnableSsl = true,
            FromEmail = "new@example.com"
        };

        await provider.UpdateConfigurationAsync("EmailSettings", newSettings);
        await Task.Delay(100);

        // Assert
        var updatedValue = monitor.CurrentValue;
        Assert.Equal("smtp.new.com", updatedValue.SmtpHost);
        Assert.Equal(465, updatedValue.SmtpPort);
    }

    [Fact]
    public void AddDatabaseOptions_WithoutBinding_ShouldStillRegister()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - Register without binding
        services.AddDatabaseOptions<EmailSettings>("EmailSettings");

        // Assert
        Assert.True(DatabaseConfigurationRegistry.IsRegistered("EmailSettings"));
    }

    [Fact]
    public void AddDatabaseOptions_ShouldSupportCustomValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiSettings:BaseUrl"] = "https://api.example.com",
                ["ApiSettings:TimeoutSeconds"] = "0" // Invalid
            })
            .Build();

        services.AddDatabaseOptions<ApiSettings>("ApiSettings", configuration)
            .Validate(settings =>
            {
                return settings.TimeoutSeconds > 0;
            }, "Timeout must be greater than 0")
            .ValidateOnStart();

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        Assert.Throws<OptionsValidationException>(() =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ApiSettings>>();
            _ = options.Value;
        });
    }

    [Fact]
    public void LibraryExtensionMethod_ShouldRegisterEverything()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["EmailSettings:EnableSsl"] = "true",
                ["EmailSettings:FromEmail"] = "test@example.com"
            })
            .Build();

        // Act - Use library extension method
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEmailLibrary(configuration);

        // Build provider AFTER registering the section
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["EmailSettings:EnableSsl"] = "true",
                ["EmailSettings:FromEmail"] = "test@example.com"
            })
            .AddDatabaseConfiguration(_connectionString);

        var finalConfig = configBuilder.Build();
        services.AddSingleton<IConfiguration>(finalConfig);
        services.Configure<EmailLibraryExtensions.EmailSettings>(finalConfig.GetSection("EmailSettings"));

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.True(DatabaseConfigurationRegistry.IsRegistered("EmailSettings"));

        var emailService = serviceProvider.GetService<IEmailService>();
        Assert.NotNull(emailService);

        var options = serviceProvider.GetRequiredService<IOptions<EmailLibraryExtensions.EmailSettings>>();
        Assert.NotNull(options.Value);
        Assert.Equal("smtp.example.com", options.Value.SmtpHost);
    }

    public void Dispose()
    {
        // Clean up test database
        try
        {
            using var context = new ConfigurationDbContext(_dbOptions);
            context.Database.EnsureDeleted();
        }
        catch
        {
            // Ignore cleanup errors
        }

        // Clear registry
        DatabaseConfigurationRegistry.Clear();
    }

    // Test classes with validation
    private class EmailSettings
    {
        [Required]
        [MinLength(3)]
        public string SmtpHost { get; set; } = string.Empty;

        [Range(1, 65535)]
        public int SmtpPort { get; set; }

        public bool EnableSsl { get; set; }

        [EmailAddress]
        public string FromEmail { get; set; } = string.Empty;

        public string? FromName { get; set; }
    }

    private class FeatureFlags
    {
        public bool EnableNewUI { get; set; }
        public bool EnableBetaFeatures { get; set; }
    }

    private class ApiSettings
    {
        [Required]
        [Url]
        public string BaseUrl { get; set; } = string.Empty;

        [Range(1, 300)]
        public int TimeoutSeconds { get; set; } = 30;
    }
}

// Example library extension
public static class EmailLibraryExtensions
{
    public static IServiceCollection AddEmailLibrary(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IEmailService, EmailService>();

        return services;
    }

    public class EmailSettings
    {
        [Required]
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; }
        public bool EnableSsl { get; set; }
        public string FromEmail { get; set; } = string.Empty;
    }
}

public interface IEmailService { }
public class EmailService : IEmailService { }
