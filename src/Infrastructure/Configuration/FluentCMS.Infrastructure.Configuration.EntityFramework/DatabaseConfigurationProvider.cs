namespace FluentCMS.Infrastructure.Configuration.EntityFramework;

/// <summary>
/// Custom configuration provider that reads from database using EF Core
/// </summary>
public class DatabaseConfigurationProvider(DbContextOptions<ConfigurationDbContext> dbOptions, TimeSpan reloadInterval) : ConfigurationProvider, IDisposable
{
    private readonly InMemoryCache _cache = new();
    private Timer? _reloadTimer;
    private bool _disposed;

    public override void Load()
    {
        using var context = new ConfigurationDbContext(dbOptions);

        // Ensure database is created
        context.Database.EnsureCreated();

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

    public void Dispose()
    {
        if (!_disposed)
        {
            _reloadTimer?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
