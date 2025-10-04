using FluentCMS.Repositories.Abstractions.Configuration;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Internal registry for managing database configurations across areas.
/// Handles storage and retrieval of database manager configurations.
/// </summary>
internal static class DatabaseConfigurationRegistry
{
    private static readonly Dictionary<Type, DatabaseAreaConfiguration> _areaConfigurations = new();
    private static DatabaseAreaConfiguration? _defaultConfiguration;

    /// <summary>
    /// Registers database configurations from the database manager options.
    /// </summary>
    /// <param name="options">The configured database manager options</param>
    public static void RegisterConfigurations(DatabaseManagerOptions options)
    {
        _areaConfigurations.Clear();
        _defaultConfiguration = options.DefaultConfiguration;

        foreach (var kvp in options.AreaConfigurations)
        {
            _areaConfigurations[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Gets the configuration for the specified area type.
    /// Returns area-specific configuration if found, otherwise default configuration.
    /// </summary>
    /// <param name="areaType">The area type to get configuration for</param>
    /// <returns>The database area configuration, or null if none found</returns>
    public static DatabaseAreaConfiguration? GetConfiguration(Type areaType)
    {
        // Check for area-specific configuration first
        if (_areaConfigurations.TryGetValue(areaType, out var areaConfig))
        {
            return areaConfig;
        }

        // Fall back to default configuration
        return _defaultConfiguration;
    }
}
