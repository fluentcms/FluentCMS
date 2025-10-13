namespace FluentCMS.Infrastructure.Repositories.EntityFramework.Configuration;

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

    private readonly IServiceCollection _serviceDescriptors;

    internal DatabaseManagerOptions(IServiceCollection services)
    {
        _serviceDescriptors = services;
    }

    /// <summary>
    /// Configures the default database to be used by DbContexts that don't have a specific marker interface
    /// </summary>
    /// <returns>A configuration builder for the default database</returns>
    public DatabaseConfigurationBuilder Default()
    {
        _defaultConfiguration = new DatabaseConfiguration(_serviceDescriptors, typeof(IDefaultDatabaseArea));
        var builder = new DatabaseConfigurationBuilder(_defaultConfiguration, _serviceDescriptors);
        return builder;
    }

    /// <summary>
    /// Configures a specific database for DbContexts that implement the marker interface TMarker
    /// </summary>
    /// <typeparam name="TMarker">The marker interface type used to identify which DbContexts should use this configuration</typeparam>
    /// <returns>A configuration builder for the specific database</returns>
    public DatabaseConfigurationBuilder For<TMarker>() where TMarker : IDatabaseArea
    {
        var config = new DatabaseConfiguration(_serviceDescriptors, typeof(TMarker));
        _configurations[typeof(TMarker)] = config;

        var builder = new DatabaseConfigurationBuilder(config, _serviceDescriptors);
        return builder;
    }

    /// <summary>
    /// Gets the default database configuration
    /// </summary>
    /// <returns>The default configuration</returns>
    public DatabaseConfiguration GetDefaultConfiguration()
    {
        return _defaultConfiguration ??
            throw new InvalidOperationException("No default database configuration found. Use Default() to define the default database configuration");
    }

    /// <summary>
    /// Gets the configuration for a specific marker type
    /// </summary>
    /// <param name="markerType">The marker type to get configuration for</param>
    /// <returns>The database configuration for the marker</returns>
    public DatabaseConfiguration GetConfigurationForMarker(Type markerType)
    {
        ArgumentNullException.ThrowIfNull(markerType);

        if (_configurations.TryGetValue(markerType, out var config))
            return config;

        return GetDefaultConfiguration();
    }

    /// <summary>
    /// Gets all registered marker types that have specific database configurations
    /// </summary>
    /// <returns>Collection of marker types</returns>
    public IEnumerable<Type> GetRegisteredMarkers()
    {
        return _configurations.Keys;
    }
}
