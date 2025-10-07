using FluentCMS.Configuration.Tests.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Configuration;

/// <summary>
/// Custom configuration provider that reads from database using EF Core
/// </summary>
public class DatabaseConfigurationProvider(
    DbContextOptions<ConfigurationDbContext> dbOptions,
    TimeSpan reloadInterval,
    IConfiguration? seedConfiguration = null,
    List<string>? dynamicSections = null) : ConfigurationProvider, IDisposable
{
    private readonly InMemoryCache _cache = new();
    private readonly List<string> _dynamicSections = dynamicSections ?? [];
    private Timer? _reloadTimer;
    private bool _disposed;

    public override void Load()
    {
        using var context = new ConfigurationDbContext(dbOptions);

        // Ensure database is created
        context.Database.EnsureCreated();

        // Seed dynamic sections from appsettings.json if they don't exist
        SeedDynamicSections(context);

        LoadConfigurationsFromDatabase(context);

        // Setup automatic reload if interval is specified
        if (reloadInterval > TimeSpan.Zero)
        {
            _reloadTimer = new Timer(
                _ => ReloadConfigurations(),
                null,
                reloadInterval,
                reloadInterval);
        }
    }

    private void SeedDynamicSections(ConfigurationDbContext context)
    {
        if (seedConfiguration == null || _dynamicSections.Count == 0)
            return;

        foreach (var section in _dynamicSections)
        {
            // Check if section already exists in database
            if (context.Configurations.Any(c => c.Section == section))
                continue;

            // Get the section from appsettings.json
            var configSection = seedConfiguration.GetSection(section);
            if (!configSection.Exists())
                continue;

            // Convert section to dictionary
            var sectionData = GetSectionAsDictionary(configSection);
            if (sectionData.Count == 0)
                continue;

            // Serialize to JSON
            var json = JsonSerializer.Serialize(sectionData);

            // Add to database
            context.Configurations.Add(new ConfigurationEntity
            {
                Section = section,
                Value = json,
                Type = "System.Object"
            });
        }

        // Save all seeded sections
        context.SaveChanges();
    }

    private static Dictionary<string, object?> GetSectionAsDictionary(IConfigurationSection section)
    {
        var result = new Dictionary<string, object?>();

        var children = section.GetChildren().ToList();

        if (children.Count == 0)
        {
            // Leaf value
            return new Dictionary<string, object?>();
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

    private void LoadConfigurationsFromDatabase(ConfigurationDbContext context)
    {
        var configurations = context.Configurations.ToList();
        var newData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var config in configurations)
        {
            try
            {
                // Cache the configuration
                _cache.Set(config.Section, config);

                // Parse JSON and flatten to configuration keys
                var jsonNode = JsonNode.Parse(config.Value);
                if (jsonNode is JsonObject jsonObject)
                {
                    FlattenJson(newData, config.Section, jsonObject);
                }
            }
            catch (JsonException)
            {
                // Skip invalid JSON
                continue;
            }
        }

        Data = newData;
    }

    private void ReloadConfigurations()
    {
        try
        {
            using var context = new ConfigurationDbContext(dbOptions);
            var oldData = Data;

            LoadConfigurationsFromDatabase(context);

            // Trigger change notification if data has changed
            if (!DictionariesEqual(oldData, Data))
            {
                OnReload();
            }
        }
        catch
        {
            // Ignore reload errors
        }
    }

    private static bool DictionariesEqual(IDictionary<string, string?> dict1, IDictionary<string, string?> dict2)
    {
        if (dict1.Count != dict2.Count)
            return false;

        foreach (var kvp in dict1)
        {
            if (!dict2.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
                return false;
        }

        return true;
    }

    private static void FlattenJson(Dictionary<string, string?> data, string prefix, JsonObject obj)
    {
        foreach (var kvp in obj)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}:{kvp.Key}";

            switch (kvp.Value)
            {
                case JsonObject childObj:
                    FlattenJson(data, key, childObj);
                    break;

                case JsonArray arr:
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var item = arr[i];
                        if (item is JsonObject itemObj)
                        {
                            FlattenJson(data, $"{key}:{i}", itemObj);
                        }
                        else
                        {
                            data[$"{key}:{i}"] = item?.ToJsonString();
                        }
                    }
                    break;

                default:
                    data[key] = kvp.Value?.GetValueKind() is JsonValueKind.String
                        ? kvp.Value.GetValue<string>()
                        : kvp.Value?.ToJsonString();
                    break;
            }
        }
    }

    /// <summary>
    /// Updates a configuration in the database and triggers reload
    /// </summary>
    public async Task UpdateConfigurationAsync(string section, object value, CancellationToken cancellationToken = default)
    {
        using var context = new ConfigurationDbContext(dbOptions);

        var json = JsonSerializer.Serialize(value);
        var typeName = value.GetType().FullName ?? value.GetType().Name;

        var existing = await context.Configurations
            .FirstOrDefaultAsync(c => c.Section == section, cancellationToken);

        if (existing != null)
        {
            existing.Value = json;
            existing.Type = typeName;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            context.Configurations.Add(new ConfigurationEntity
            {
                Section = section,
                Value = json,
                Type = typeName
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        // Update cache
        var entity = existing ?? context.Configurations.Local.First(c => c.Section == section);
        _cache.Set(section, entity);

        // Reload configuration to update IConfiguration
        Load();
        OnReload();
    }

    /// <summary>
    /// Gets a configuration from cache or database
    /// </summary>
    public async Task<T?> GetConfigurationAsync<T>(string section, CancellationToken cancellationToken = default) where T : class
    {
        // Try cache first
        if (_cache.TryGet<ConfigurationEntity>(section, out var cached))
        {
            return JsonSerializer.Deserialize<T>(cached.Value);
        }

        // Load from database
        using var context = new ConfigurationDbContext(dbOptions);
        var entity = await context.Configurations
            .FirstOrDefaultAsync(c => c.Section == section, cancellationToken);

        if (entity == null)
            return null;

        _cache.Set(section, entity);
        return JsonSerializer.Deserialize<T>(entity.Value);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _reloadTimer?.Dispose();
            _disposed = true;
        }
    }
}
