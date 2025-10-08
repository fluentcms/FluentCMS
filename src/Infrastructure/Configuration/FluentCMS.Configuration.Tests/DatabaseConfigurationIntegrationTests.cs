using FluentCMS.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Configuration.Tests;

public class DatabaseConfigurationIntegrationTests : IDisposable
{
    private readonly string _connectionString;
    private readonly DbContextOptions<ConfigurationDbContext> _dbOptions;

    public DatabaseConfigurationIntegrationTests()
    {
        _connectionString = $"Data Source=test_integration_{Guid.NewGuid()}.db";
        _dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        // Clear registry before each test
        DatabaseConfigurationRegistry.Clear();
    }

    [Fact]
    public async Task CompleteWorkflow_SeedUpdateAndRead_ShouldWork()
    {
        // Arrange - Setup initial configuration
        var initialConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.initial.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["EmailSettings:EnableSsl"] = "true",
                ["EmailSettings:FromEmail"] = "initial@example.com"
            })
            .Build();

        // Step 1: Register section and add seeding
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", initialConfig);
        services.AddDatabaseConfigurationSeeding(_dbOptions, initialConfig, ["EmailSettings"]);

        var serviceProvider = services.BuildServiceProvider();

        // Step 2: Run seeding
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        var seedingService = hostedServices.OfType<ConfigurationSeedingHostedService>().First();
        await seedingService.StartAsync(CancellationToken.None);

        // Step 3: Create configuration with database provider
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddDatabaseConfiguration(_dbOptions, TimeSpan.FromSeconds(1)); // Fast reload for testing
        var finalConfig = configBuilder.Build();

        // Step 4: Register final configuration and services
        services.AddSingleton<IConfiguration>(finalConfig);
        services.Configure<EmailSettings>(finalConfig.GetSection("EmailSettings"));
        services.AddDatabaseConfigurationServices(finalConfig);

        var finalServiceProvider = services.BuildServiceProvider();

        // Act & Assert - Initial values should be loaded from database
        var monitor = finalServiceProvider.GetRequiredService<IOptionsMonitor<EmailSettings>>();
        var provider = finalServiceProvider.GetRequiredService<DatabaseConfigurationProvider>();

        var initialSettings = monitor.CurrentValue;
        Assert.Equal("smtp.initial.com", initialSettings.SmtpHost);
        Assert.Equal(587, initialSettings.SmtpPort);
        Assert.True(initialSettings.EnableSsl);
        Assert.Equal("initial@example.com", initialSettings.FromEmail);

        // Act - Update configuration at runtime
        var updatedSettings = new EmailSettings
        {
            SmtpHost = "smtp.updated.com",
            SmtpPort = 465,
            EnableSsl = false,
            FromEmail = "updated@example.com",
            FromName = "Updated Name"
        };

        await provider.UpdateConfigurationAsync("EmailSettings", updatedSettings);
        
        // Give time for reload to trigger
        await Task.Delay(1500);

        // Assert - Configuration should be updated
        var newSettings = monitor.CurrentValue;
        Assert.Equal("smtp.updated.com", newSettings.SmtpHost);
        Assert.Equal(465, newSettings.SmtpPort);
        Assert.False(newSettings.EnableSsl);
        Assert.Equal("updated@example.com", newSettings.FromEmail);
        Assert.Equal("Updated Name", newSettings.FromName);

        // Verify database was updated
        using var context = new ConfigurationDbContext(_dbOptions);
        var dbConfig = await context.Configurations.FirstAsync(c => c.Section == "EmailSettings");
        Assert.Contains("smtp.updated.com", dbConfig.Value);
        Assert.Contains("465", dbConfig.Value);
    }

    [Fact]
    public async Task MultipleLibraries_ShouldWorkIndependently()
    {
        // Arrange - Setup configurations for multiple libraries
        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Email library config
                ["EmailSettings:SmtpHost"] = "smtp.email.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["EmailSettings:FromEmail"] = "email@example.com",
                
                // API library config
                ["ApiSettings:BaseUrl"] = "https://api.example.com",
                ["ApiSettings:TimeoutSeconds"] = "30",
                ["ApiSettings:RetryCount"] = "3",
                
                // Feature flags config
                ["FeatureFlags:EnableNewUI"] = "true",
                ["FeatureFlags:EnableBetaFeatures"] = "false",
                ["FeatureFlags:MaxUploadSizeMB"] = "50"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Register multiple library configurations
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", seedConfig)
            .ValidateDataAnnotations();
        
        services.AddDatabaseOptions<ApiSettings>("ApiSettings", seedConfig)
            .ValidateDataAnnotations();
        
        services.AddDatabaseOptions<FeatureFlags>("FeatureFlags", seedConfig)
            .ValidateDataAnnotations();

        // Add seeding for all registered sections
        services.AddDatabaseConfigurationSeeding(_dbOptions, seedConfig);

        var serviceProvider = services.BuildServiceProvider();

        // Run seeding
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        var seedingService = hostedServices.OfType<ConfigurationSeedingHostedService>().First();
        await seedingService.StartAsync(CancellationToken.None);

        // Setup final configuration
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddDatabaseConfiguration(_dbOptions);
        var finalConfig = configBuilder.Build();

        services.AddSingleton<IConfiguration>(finalConfig);
        services.Configure<EmailSettings>(finalConfig.GetSection("EmailSettings"));
        services.Configure<ApiSettings>(finalConfig.GetSection("ApiSettings"));
        services.Configure<FeatureFlags>(finalConfig.GetSection("FeatureFlags"));

        var finalServiceProvider = services.BuildServiceProvider();

        // Act & Assert - All configurations should be available
        var emailOptions = finalServiceProvider.GetRequiredService<IOptions<EmailSettings>>();
        var apiOptions = finalServiceProvider.GetRequiredService<IOptions<ApiSettings>>();
        var featureOptions = finalServiceProvider.GetRequiredService<IOptions<FeatureFlags>>();

        // Email settings
        Assert.Equal("smtp.email.com", emailOptions.Value.SmtpHost);
        Assert.Equal(587, emailOptions.Value.SmtpPort);
        Assert.Equal("email@example.com", emailOptions.Value.FromEmail);

        // API settings  
        Assert.Equal("https://api.example.com", apiOptions.Value.BaseUrl);
        Assert.Equal(30, apiOptions.Value.TimeoutSeconds);
        Assert.Equal(3, apiOptions.Value.RetryCount);

        // Feature flags
        Assert.True(featureOptions.Value.EnableNewUI);
        Assert.False(featureOptions.Value.EnableBetaFeatures);
        Assert.Equal(50, featureOptions.Value.MaxUploadSizeMB);

        // Verify all sections were seeded in database
        using var context = new ConfigurationDbContext(_dbOptions);
        var sections = await context.Configurations.Select(c => c.Section).ToListAsync();
        
        Assert.Contains("EmailSettings", sections);
        Assert.Contains("ApiSettings", sections);
        Assert.Contains("FeatureFlags", sections);
        Assert.Equal(3, sections.Count);
    }

    [Fact]
    public async Task ConfigurationReload_ShouldTriggerOptionsMonitorChange()
    {
        // Arrange
        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.original.com",
                ["EmailSettings:SmtpPort"] = "587"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", seedConfig);
        services.AddDatabaseConfigurationSeeding(_dbOptions, seedConfig, ["EmailSettings"]);

        var serviceProvider = services.BuildServiceProvider();

        // Run seeding
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        var seedingService = hostedServices.OfType<ConfigurationSeedingHostedService>().First();
        await seedingService.StartAsync(CancellationToken.None);

        // Setup configuration with fast reload
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddDatabaseConfiguration(_dbOptions, TimeSpan.FromMilliseconds(500));
        var finalConfig = configBuilder.Build();

        services.AddSingleton<IConfiguration>(finalConfig);
        services.Configure<EmailSettings>(finalConfig.GetSection("EmailSettings"));
        services.AddDatabaseConfigurationServices(finalConfig);

        var finalServiceProvider = services.BuildServiceProvider();

        var monitor = finalServiceProvider.GetRequiredService<IOptionsMonitor<EmailSettings>>();
        var provider = finalServiceProvider.GetRequiredService<DatabaseConfigurationProvider>();

        // Setup change tracking
        var changeCount = 0;
        var tcs = new TaskCompletionSource<bool>();

        monitor.OnChange((settings, name) =>
        {
            changeCount++;
            if (changeCount >= 1)
            {
                tcs.SetResult(true);
            }
        });

        // Act - Update configuration directly in database (simulating external change)
        using (var context = new ConfigurationDbContext(_dbOptions))
        {
            var existing = await context.Configurations.FirstAsync(c => c.Section == "EmailSettings");
            existing.Value = """{"SmtpHost":"smtp.changed.com","SmtpPort":465,"EnableSsl":true}""";
            existing.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        // Wait for reload to trigger
        await Task.WhenAny(tcs.Task, Task.Delay(2000));

        // Assert
        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.True(changeCount >= 1);

        var updatedSettings = monitor.CurrentValue;
        Assert.Equal("smtp.changed.com", updatedSettings.SmtpHost);
        Assert.Equal(465, updatedSettings.SmtpPort);
    }

    [Fact]
    public async Task ValidationErrors_ShouldBeCaughtByOptionsValidation()
    {
        // Arrange
        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["EmailSettings:FromEmail"] = "valid@example.com"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseOptions<EmailSettings>("EmailSettings", seedConfig)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDatabaseConfigurationSeeding(_dbOptions, seedConfig, ["EmailSettings"]);

        var serviceProvider = services.BuildServiceProvider();

        // Run seeding
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        var seedingService = hostedServices.OfType<ConfigurationSeedingHostedService>().First();
        await seedingService.StartAsync(CancellationToken.None);

        // Setup configuration
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddDatabaseConfiguration(_dbOptions);
        var finalConfig = configBuilder.Build();

        services.AddSingleton<IConfiguration>(finalConfig);
        services.Configure<EmailSettings>(finalConfig.GetSection("EmailSettings"));
        services.AddDatabaseConfigurationServices(finalConfig);

        var finalServiceProvider = services.BuildServiceProvider();
        var provider = finalServiceProvider.GetRequiredService<DatabaseConfigurationProvider>();

        // Act & Assert - Update with invalid data should fail validation
        var invalidSettings = new EmailSettings
        {
            SmtpHost = "", // Required field
            SmtpPort = 70000, // Out of range
            FromEmail = "invalid-email" // Invalid email format
        };

        // The update should succeed (database level)
        await provider.UpdateConfigurationAsync("EmailSettings", invalidSettings);

        // But validation should catch it when accessed
        var monitor = finalServiceProvider.GetRequiredService<IOptionsMonitor<EmailSettings>>();
        
        Assert.Throws<OptionsValidationException>(() =>
        {
            var _ = monitor.CurrentValue;
        });
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

    // Test classes
    private class EmailSettings
    {
        [Required]
        [MinLength(3)]
        public string SmtpHost { get; set; } = string.Empty;

        [Range(1, 65535)]
        public int SmtpPort { get; set; }

        public bool EnableSsl { get; set; }

        [Required]
        [EmailAddress]
        public string FromEmail { get; set; } = string.Empty;

        public string? FromName { get; set; }
    }

    private class ApiSettings
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

    private class FeatureFlags
    {
        public bool EnableNewUI { get; set; }
        public bool EnableBetaFeatures { get; set; }

        [Range(1, 1000)]
        public int MaxUploadSizeMB { get; set; } = 10;
    }
}
