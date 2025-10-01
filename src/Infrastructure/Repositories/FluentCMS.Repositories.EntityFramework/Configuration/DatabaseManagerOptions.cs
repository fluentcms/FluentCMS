namespace FluentCMS.Repositories.EntityFramework.Configuration;

/// <summary>
/// Options class for configuring database connections for multiple DbContexts
/// Supports both default configuration and marker-based specific configurations
/// </summary>
public class DatabaseManagerOptions
{
    // Dictionary to store configurations keyed by marker type
    private readonly Dictionary<Type, DatabaseConfiguration> _configurations = [];
    
    // Default configuration for DbContexts without a specific marker
    private DatabaseConfiguration? _defaultConfiguration;

    /// <summary>
    /// Configures the default database to be used by DbContexts that don't have a specific marker interface
    /// </summary>
    /// <returns>A configuration builder for the default database</returns>
    public IDatabaseConfigurationBuilder Default()
    {
        _defaultConfiguration = new DatabaseConfiguration();
        var builder = new DatabaseConfigurationBuilder
        {
            Configuration = _defaultConfiguration
        };
        return builder;
    }

    /// <summary>
    /// Configures a specific database for DbContexts that implement the marker interface TMarker
    /// </summary>
    /// <typeparam name="TMarker">The marker interface type used to identify which DbContexts should use this configuration</typeparam>
    /// <returns>A configuration builder for the specific database</returns>
    public IDatabaseConfigurationBuilder For<TMarker>() where TMarker : class
    {
        var config = new DatabaseConfiguration { MarkerType = typeof(TMarker) };
        _configurations[typeof(TMarker)] = config;

        var builder = new DatabaseConfigurationBuilder
        {
            Configuration = config
        };
        return builder;
    }

    /// <summary>
    /// Gets the appropriate configuration for a given DbContext type
    /// First checks if the context implements any registered marker interface
    /// Falls back to the default configuration if no marker is found
    /// </summary>
    /// <param name="contextType">The DbContext type to get configuration for</param>
    /// <returns>The database configuration to use</returns>
    /// <exception cref="InvalidOperationException">Thrown when no configuration is found and no default is set</exception>
    internal DatabaseConfiguration GetConfigurationForContext(Type contextType)
    {
        ArgumentNullException.ThrowIfNull(contextType);

        // Check if the DbContext type implements any of the registered marker interfaces
        var markerType = _configurations.Keys
            .FirstOrDefault(marker => marker.IsAssignableFrom(contextType));

        // If a marker is found, return its specific configuration
        if (markerType != null)
        {
            return _configurations[markerType];
        }

        // Fall back to default configuration
        if (_defaultConfiguration == null)
        {
            throw new InvalidOperationException(
                $"No database configuration found for DbContext '{contextType.Name}' and no default configuration is set. " +
                "Please configure a default database using options.Default() or add a marker interface and configure it with options.For<TMarker>()");
        }

        return _defaultConfiguration;
    }

    /// <summary>
    /// Gets the default database configuration
    /// </summary>
    /// <returns>The default configuration, or null if not configured</returns>
    internal DatabaseConfiguration? GetDefaultConfiguration()
    {
        return _defaultConfiguration;
    }

    /// <summary>
    /// Gets the configuration for a specific marker type
    /// </summary>
    /// <param name="markerType">The marker type to get configuration for</param>
    /// <returns>The database configuration for the marker</returns>
    /// <exception cref="InvalidOperationException">Thrown when no configuration is found for the marker</exception>
    internal DatabaseConfiguration GetConfigurationForMarker(Type markerType)
    {
        ArgumentNullException.ThrowIfNull(markerType);

        if (_configurations.TryGetValue(markerType, out var config))
        {
            return config;
        }

        throw new InvalidOperationException(
            $"No database configuration found for marker type '{markerType.Name}'. " +
            "Please configure it using options.For<{markerType.Name}>()");
    }

    /// <summary>
    /// Gets all registered marker types that have specific database configurations
    /// </summary>
    /// <returns>Collection of marker types</returns>
    internal IEnumerable<Type> GetRegisteredMarkers()
    {
        return _configurations.Keys;
    }
}
