namespace FluentCMS.Infrastructure.Configuration.EntityFramework;

internal class ConfigurationDataSeeder(ConfigurationDbContext dbContext, ILogger<ConfigurationDataSeeder> logger, DatabaseConfigurationRegistry configurationRegistry, IConfiguration configuration) : BaseDataSeeder<ConfigurationDbContext>(dbContext, logger)
{
    // The configuration data seeder has the lowest priority to ensure it runs last
    public override int Priority => 0;

    public override Task<bool> ShouldSeed(CancellationToken cancellationToken = default)
    {
        // We should check for all existing records with registered sections
        // We should always attempt to seed
        return Task.FromResult(true);
    }

    public override async Task SeedData(CancellationToken cancellationToken = default)
    {
        await SeedDynamicSections(cancellationToken);
    }

    private async Task SeedDynamicSections(CancellationToken cancellationToken)
    {
        var sectionsSeeded = 0;

        foreach (var (section, type) in configurationRegistry.GetRegisteredSections())
        {
            // Check if section already exists in database
            if (await DbContext.Configurations.AnyAsync(c => c.Section == section, cancellationToken))
            {
                logger.LogDebug("Configuration section '{Section}' already exists, skipping", section);
                continue;
            }

            // Get the section from appsettings.json
            var configSection = configuration.GetSection(section);
            if (!configSection.Exists())
            {
                logger.LogWarning("Configuration section '{Section}' not found in seed configuration", section);
                continue;
            }

            // Convert section to dictionary
            var sectionData = GetSectionAsDictionary(configSection);
            if (sectionData.Count == 0)
            {
                logger.LogWarning("Configuration section '{Section}' is empty", section);
                continue;
            }

            // Serialize to JSON
            var json = JsonSerializer.Serialize(sectionData);

            // Add to database
            DbContext.Configurations.Add(new ConfigurationEntity
            {
                Section = section,
                Value = json,
                Type = type.FullName!
            });

            sectionsSeeded++;
            logger.LogDebug("Prepared section '{Section}' for seeding", section);
        }

        if (sectionsSeeded > 0)
        {
            // Save all seeded sections
            await DbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} configuration sections to database", sectionsSeeded);
        }
        else
        {
            logger.LogInformation("No new configuration sections to seed");
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
