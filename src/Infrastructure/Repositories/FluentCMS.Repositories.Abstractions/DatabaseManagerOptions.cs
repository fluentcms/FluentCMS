using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Configuration options for the Database Manager
/// </summary>
public class DatabaseManagerOptions
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<Type, IDatabaseAreaConfiguration> _areaConfigurations = [];
    private IDatabaseAreaConfiguration? _defaultConfiguration;

    internal DatabaseManagerOptions(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Configure the default database settings for areas that don't have explicit configuration
    /// </summary>
    /// <returns>Default database configuration builder</returns>
    public DefaultDatabaseConfiguration Default()
    {
        var config = new DefaultDatabaseConfiguration(_services);
        _defaultConfiguration = config;
        return config;
    }

    /// <summary>
    /// Configure database settings for a specific area
    /// </summary>
    /// <typeparam name="TArea">The database area marker interface</typeparam>
    /// <returns>Area-specific database configuration builder</returns>
    public DatabaseAreaConfiguration<TArea> For<TArea>()
        where TArea : class, IDatabaseArea
    {
        var config = new DatabaseAreaConfiguration<TArea>(_services);
        _areaConfigurations[typeof(TArea)] = config;
        return config;
    }

    /// <summary>
    /// Get the configuration for a specific area, or default if not configured
    /// </summary>
    /// <param name="areaType">The area type</param>
    /// <returns>The configuration for the area</returns>
    internal IDatabaseAreaConfiguration? GetConfiguration(Type areaType)
    {
        return _areaConfigurations.TryGetValue(areaType, out var config) ? config : _defaultConfiguration;
    }

    /// <summary>
    /// Get all area configurations
    /// </summary>
    internal IReadOnlyDictionary<Type, IDatabaseAreaConfiguration> GetAreaConfigurations()
    {
        return _areaConfigurations.AsReadOnly();
    }

    /// <summary>
    /// Get the default configuration
    /// </summary>
    internal IDatabaseAreaConfiguration? GetDefaultConfiguration()
    {
        return _defaultConfiguration;
    }
}
