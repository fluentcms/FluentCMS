using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FluentCMS.Configuration;

/// <summary>
/// Hosted service responsible for seeding configuration data from appsettings.json to database
/// </summary>
public class ConfigurationSeedingHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConfigurationSeedingHostedService> _logger;
    private readonly ConfigurationSeedingOptions _options;

    public ConfigurationSeedingHostedService(
        IServiceProvider serviceProvider,
        ILogger<ConfigurationSeedingHostedService> logger,
        ConfigurationSeedingOptions options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.SeedConfiguration == null || _options.DynamicSections.Count == 0)
        {
            _logger.LogInformation("Configuration seeding skipped: No seed configuration or dynamic sections provided");
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();

            // Ensure database is created
            await context.Database.EnsureCreatedAsync(cancellationToken);

            await SeedDynamicSections(context, cancellationToken);

            _logger.LogInformation("Configuration seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during configuration seeding");
            // Don't throw here to avoid crashing the application startup
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task SeedDynamicSections(ConfigurationDbContext context, CancellationToken cancellationToken)
    {
        var sectionsSeeded = 0;

        foreach (var section in _options.DynamicSections)
        {
            // Check if section already exists in database
            if (await context.Configurations.AnyAsync(c => c.Section == section, cancellationToken))
            {
                _logger.LogDebug("Configuration section '{Section}' already exists, skipping", section);
                continue;
            }

            // Get the section from appsettings.json
            var configSection = _options.SeedConfiguration!.GetSection(section);
            if (!configSection.Exists())
            {
                _logger.LogWarning("Configuration section '{Section}' not found in seed configuration", section);
                continue;
            }

            // Convert section to dictionary
            var sectionData = GetSectionAsDictionary(configSection);
            if (sectionData.Count == 0)
            {
                _logger.LogWarning("Configuration section '{Section}' is empty", section);
                continue;
            }

            // Serialize to JSON
            var json = JsonSerializer.Serialize(sectionData);

            // Add to database
            context.Configurations.Add(new ConfigurationEntity
            {
                Section = section,
                Value = json,
                Type = "System.Object"
            });

            sectionsSeeded++;
            _logger.LogDebug("Prepared section '{Section}' for seeding", section);
        }

        if (sectionsSeeded > 0)
        {
            // Save all seeded sections
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded {Count} configuration sections to database", sectionsSeeded);
        }
        else
        {
            _logger.LogInformation("No new configuration sections to seed");
        }
    }

    private static Dictionary<string, object?> GetSectionAsDictionary(IConfigurationSection section)
    {
        var result = new Dictionary<string, object?>();

        var children = section.GetChildren().ToList();

        if (children.Count == 0)
        {
            // Leaf value
            return [];
        }

        foreach (var child in children)
        {
            var childChildren = child.GetChildren().ToList();

            if (childChildren.Count == 0)
            {
                // Simple value
                result[child.Key] = child.Value;
            }
            else
            {
                // Nested object or array
                if (int.TryParse(child.Key, out _))
                {
                    // This is an array element - handle specially
                    result[child.Key] = GetSectionAsDictionary(child);
                }
                else
                {
                    // Nested object
                    result[child.Key] = GetSectionAsDictionary(child);
                }
            }
        }

        return result;
    }
}

/// <summary>
/// Options for configuration seeding hosted service
/// </summary>
public class ConfigurationSeedingOptions
{
    /// <summary>
    /// Configuration source to seed from (typically appsettings.json)
    /// </summary>
    public IConfiguration? SeedConfiguration { get; set; }

    /// <summary>
    /// List of configuration sections to seed to database
    /// </summary>
    public List<string> DynamicSections { get; set; } = [];
}
