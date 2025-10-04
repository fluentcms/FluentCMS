namespace FluentCMS.Repositories.Abstractions.Configuration;

/// <summary>
/// Configuration options for the database manager, allowing per-area database setup and defaults.
/// </summary>
public class DatabaseManagerOptions
{
    private readonly Dictionary<Type, DatabaseAreaConfiguration> _areaConfigurations = [];

    /// <summary>
    /// Gets the default area configuration that applies to all areas not explicitly configured.
    /// </summary>
    public DatabaseAreaConfiguration? DefaultConfiguration { get; private set; }

    /// <summary>
    /// Gets the collection of area-specific configurations.
    /// </summary>
    public IReadOnlyDictionary<Type, DatabaseAreaConfiguration> AreaConfigurations => _areaConfigurations;

    /// <summary>
    /// Configures the default database settings for areas that don't have specific configurations.
    /// </summary>
    /// <returns>A configuration instance for the default database</returns>
    public DatabaseAreaConfiguration Default()
    {
        DefaultConfiguration = new DatabaseAreaConfiguration();
        return DefaultConfiguration;
    }

    /// <summary>
    /// Configures database settings for a specific area type that implements IDatabaseArea.
    /// </summary>
    /// <typeparam name="TArea">The database area type to configure</typeparam>
    /// <returns>A configuration instance for the specified area</returns>
    public DatabaseAreaConfiguration For<TArea>() where TArea : IDatabaseArea
    {
        var areaType = typeof(TArea);
        if (!_areaConfigurations.TryGetValue(areaType, out var configuration))
        {
            configuration = new DatabaseAreaConfiguration();
            _areaConfigurations[areaType] = configuration;
        }
        return configuration;
    }
}
