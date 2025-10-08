using FluentCMS.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Configuration.Tests;

public class ConfigurationSeedingHostedServiceTests : IDisposable
{
    private readonly string _connectionString;
    private readonly DbContextOptions<ConfigurationDbContext> _dbOptions;

    public ConfigurationSeedingHostedServiceTests()
    {
        _connectionString = $"Data Source=test_hosted_{Guid.NewGuid()}.db";
        _dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        // Clear registry before each test
        DatabaseConfigurationRegistry.Clear();
    }

    [Fact]
    public async Task StartAsync_ShouldSeedConfigurationSections()
    {
        // Arrange
        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["EmailSettings:EnableSsl"] = "true",
                ["FeatureFlags:EnableNewUI"] = "true",
                ["FeatureFlags:EnableBetaFeatures"] = "false"
            })
            .Build();

        var options = new ConfigurationSeedingOptions
        {
            SeedConfiguration = seedConfig,
            DynamicSections = ["EmailSettings", "FeatureFlags"]
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ConfigurationDbContext>(opts => opts.UseSqlite(_connectionString));
        services.AddSingleton(options);

        var serviceProvider = services.BuildServiceProvider();
        var hostedService = new ConfigurationSeedingHostedService(
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<ConfigurationSeedingHostedService>>(),
            options);

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        using var context = new ConfigurationDbContext(_dbOptions);
        var configurations = await context.Configurations.ToListAsync();

        Assert.Equal(2, configurations.Count);
        
        var emailConfig = configurations.First(c => c.Section == "EmailSettings");
        Assert.Contains("smtp.example.com", emailConfig.Value);
        Assert.Contains("587", emailConfig.Value);
        
        var featureConfig = configurations.First(c => c.Section == "FeatureFlags");
        Assert.Contains("EnableNewUI", featureConfig.Value);
        Assert.Contains("true", featureConfig.Value);
    }

    [Fact]
    public async Task StartAsync_ShouldSkipExistingSections()
    {
        // Arrange - Pre-populate database
        using (var setupContext = new ConfigurationDbContext(_dbOptions))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Configurations.Add(new ConfigurationEntity
            {
                Section = "EmailSettings",
                Value = """{"SmtpHost":"existing.com","SmtpPort":25}""",
                Type = "System.Object"
            });
            await setupContext.SaveChangesAsync();
        }

        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.new.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["FeatureFlags:EnableNewUI"] = "true"
            })
            .Build();

        var options = new ConfigurationSeedingOptions
        {
            SeedConfiguration = seedConfig,
            DynamicSections = ["EmailSettings", "FeatureFlags"]
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ConfigurationDbContext>(opts => opts.UseSqlite(_connectionString));

        var serviceProvider = services.BuildServiceProvider();
        var hostedService = new ConfigurationSeedingHostedService(
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<ConfigurationSeedingHostedService>>(),
            options);

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        using var context = new ConfigurationDbContext(_dbOptions);
        var configurations = await context.Configurations.ToListAsync();

        Assert.Equal(2, configurations.Count);
        
        // EmailSettings should not be updated (existing)
        var emailConfig = configurations.First(c => c.Section == "EmailSettings");
        Assert.Contains("existing.com", emailConfig.Value);
        Assert.DoesNotContain("smtp.new.com", emailConfig.Value);
        
        // FeatureFlags should be added (new)
        var featureConfig = configurations.First(c => c.Section == "FeatureFlags");
        Assert.Contains("EnableNewUI", featureConfig.Value);
    }

    [Fact]
    public async Task StartAsync_ShouldSkipMissingSections()
    {
        // Arrange
        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                // FeatureFlags section is missing
            })
            .Build();

        var options = new ConfigurationSeedingOptions
        {
            SeedConfiguration = seedConfig,
            DynamicSections = ["EmailSettings", "FeatureFlags", "MissingSection"]
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ConfigurationDbContext>(opts => opts.UseSqlite(_connectionString));

        var serviceProvider = services.BuildServiceProvider();
        var hostedService = new ConfigurationSeedingHostedService(
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<ConfigurationSeedingHostedService>>(),
            options);

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        using var context = new ConfigurationDbContext(_dbOptions);
        var configurations = await context.Configurations.ToListAsync();

        Assert.Single(configurations);
        Assert.Equal("EmailSettings", configurations[0].Section);
    }

    [Fact]
    public async Task StartAsync_ShouldSkipEmptySections()
    {
        // Arrange
        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com"
                // EmptySection intentionally missing or with no meaningful values
            })
            .Build();

        var options = new ConfigurationSeedingOptions
        {
            SeedConfiguration = seedConfig,
            DynamicSections = ["EmailSettings", "EmptySection", "NonExistentSection"]
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ConfigurationDbContext>(opts => opts.UseSqlite(_connectionString));

        var serviceProvider = services.BuildServiceProvider();
        var hostedService = new ConfigurationSeedingHostedService(
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<ConfigurationSeedingHostedService>>(),
            options);

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert - Only EmailSettings should be seeded, empty/missing sections should be skipped
        using var context = new ConfigurationDbContext(_dbOptions);
        var configurations = await context.Configurations.ToListAsync();

        Assert.Single(configurations);
        Assert.Equal("EmailSettings", configurations[0].Section);
    }

    [Fact]
    public async Task StartAsync_ShouldHandleComplexNestedConfiguration()
    {
        // Arrange
        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ComplexSettings:Database:ConnectionString"] = "Server=localhost;Database=test",
                ["ComplexSettings:Database:CommandTimeout"] = "30",
                ["ComplexSettings:Cache:Enabled"] = "true",
                ["ComplexSettings:Cache:ExpirationMinutes"] = "60",
                ["ComplexSettings:Features:0:Name"] = "Feature1",
                ["ComplexSettings:Features:0:Enabled"] = "true",
                ["ComplexSettings:Features:1:Name"] = "Feature2",
                ["ComplexSettings:Features:1:Enabled"] = "false"
            })
            .Build();

        var options = new ConfigurationSeedingOptions
        {
            SeedConfiguration = seedConfig,
            DynamicSections = ["ComplexSettings"]
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ConfigurationDbContext>(opts => opts.UseSqlite(_connectionString));

        var serviceProvider = services.BuildServiceProvider();
        var hostedService = new ConfigurationSeedingHostedService(
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<ConfigurationSeedingHostedService>>(),
            options);

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        using var context = new ConfigurationDbContext(_dbOptions);
        var configuration = await context.Configurations.FirstAsync(c => c.Section == "ComplexSettings");

        Assert.Contains("Database", configuration.Value);
        Assert.Contains("localhost", configuration.Value);
        Assert.Contains("Cache", configuration.Value);
        Assert.Contains("Features", configuration.Value);
        Assert.Contains("Feature1", configuration.Value);
        Assert.Contains("Feature2", configuration.Value);
    }

    [Fact]
    public async Task StartAsync_ShouldNotFailWhenNoSeedConfigurationProvided()
    {
        // Arrange
        var options = new ConfigurationSeedingOptions
        {
            SeedConfiguration = null,
            DynamicSections = ["EmailSettings"]
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ConfigurationDbContext>(opts => opts.UseSqlite(_connectionString));

        var serviceProvider = services.BuildServiceProvider();
        var hostedService = new ConfigurationSeedingHostedService(
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<ConfigurationSeedingHostedService>>(),
            options);

        // Act & Assert - Should not throw
        await hostedService.StartAsync(CancellationToken.None);

        // Ensure database is created for verification
        using var context = new ConfigurationDbContext(_dbOptions);
        await context.Database.EnsureCreatedAsync();
        var configurations = await context.Configurations.ToListAsync();
        Assert.Empty(configurations);
    }

    [Fact]
    public async Task StartAsync_ShouldNotFailWhenNoDynamicSectionsProvided()
    {
        // Arrange
        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com"
            })
            .Build();

        var options = new ConfigurationSeedingOptions
        {
            SeedConfiguration = seedConfig,
            DynamicSections = [] // Empty list
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ConfigurationDbContext>(opts => opts.UseSqlite(_connectionString));

        var serviceProvider = services.BuildServiceProvider();
        var hostedService = new ConfigurationSeedingHostedService(
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<ConfigurationSeedingHostedService>>(),
            options);

        // Act & Assert - Should not throw
        await hostedService.StartAsync(CancellationToken.None);

        // Ensure database is created for verification
        using var context = new ConfigurationDbContext(_dbOptions);
        await context.Database.EnsureCreatedAsync();
        var configurations = await context.Configurations.ToListAsync();
        Assert.Empty(configurations);
    }

    [Fact]
    public async Task StartAsync_ShouldHandleArrayConfigurations()
    {
        // Arrange
        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArraySettings:Items:0"] = "Item1",
                ["ArraySettings:Items:1"] = "Item2",
                ["ArraySettings:Items:2"] = "Item3",
                ["ArraySettings:Numbers:0"] = "1",
                ["ArraySettings:Numbers:1"] = "2",
                ["ArraySettings:Numbers:2"] = "3"
            })
            .Build();

        var options = new ConfigurationSeedingOptions
        {
            SeedConfiguration = seedConfig,
            DynamicSections = ["ArraySettings"]
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ConfigurationDbContext>(opts => opts.UseSqlite(_connectionString));

        var serviceProvider = services.BuildServiceProvider();
        var hostedService = new ConfigurationSeedingHostedService(
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<ConfigurationSeedingHostedService>>(),
            options);

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        using var context = new ConfigurationDbContext(_dbOptions);
        var configuration = await context.Configurations.FirstAsync(c => c.Section == "ArraySettings");

        Assert.Contains("Items", configuration.Value);
        Assert.Contains("Item1", configuration.Value);
        Assert.Contains("Item2", configuration.Value);
        Assert.Contains("Item3", configuration.Value);
        Assert.Contains("Numbers", configuration.Value);
        Assert.Contains("\"0\":\"1\"", configuration.Value);
        Assert.Contains("\"1\":\"2\"", configuration.Value);
        Assert.Contains("\"2\":\"3\"", configuration.Value);
    }

    [Fact]
    public async Task EndToEnd_SeedingAndProviderIntegration()
    {
        // Arrange
        var seedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "smtp.example.com",
                ["EmailSettings:SmtpPort"] = "587",
                ["EmailSettings:EnableSsl"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Add seeding service
        services.AddDatabaseConfigurationSeeding(
            _dbOptions,
            seedConfiguration: seedConfig,
            dynamicSections: ["EmailSettings"]
        );

        var serviceProvider = services.BuildServiceProvider();

        // Start hosted service
        var hostedServices = serviceProvider.GetServices<IHostedService>();
        var seedingService = hostedServices.OfType<ConfigurationSeedingHostedService>().First();
        await seedingService.StartAsync(CancellationToken.None);

        // Act - Use database configuration provider
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddDatabaseConfiguration(_dbOptions);
        var configuration = configBuilder.Build();

        // Assert
        Assert.Equal("smtp.example.com", configuration["EmailSettings:SmtpHost"]);
        Assert.Equal("587", configuration["EmailSettings:SmtpPort"]);
        Assert.Equal("true", configuration["EmailSettings:EnableSsl"]);
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
}
