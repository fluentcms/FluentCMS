using System.Collections.Concurrent;

namespace FluentCMS.Repositories.Abstractions;

// Configuration for a database connection
public class DatabaseConnectionConfig
{
    public Func<IDataContext> Factory { get; set; } = null!;
    public DataSeedingOptions? SeedingOptions { get; set; }
}

// Interface for building database manager
public interface IDatabaseManagerBuilder
{
    IDatabaseManagerBuilder Default(Action<IDatabaseConnectionBuilder> configure);
    IDatabaseManagerBuilder For<TArea>(Action<IDatabaseConnectionBuilder> configure) where TArea : IDatabaseArea;
    IDatabaseManager Build();
}

// Interface for configuring a specific database connection
public interface IDatabaseConnectionBuilder
{
    void SetFactory(Func<IDataContext> factory);
    void EnableDataSeeding(Action<DataSeedingOptions> configure);
}

// Interface for the database manager
public interface IDatabaseManager
{
    IDataContext CreateDataContextForArea<TArea>() where TArea : IDatabaseArea;
    bool IsSeedingEnabledForArea<TArea>() where TArea : IDatabaseArea;
    DataSeedingOptions? GetSeedingOptionsForArea<TArea>() where TArea : IDatabaseArea;
}

// Implementation of database manager
public class DatabaseManager(ConcurrentDictionary<Type, DatabaseConnectionConfig> areaConfigurations, DatabaseConnectionConfig? defaultConfiguration) : IDatabaseManager
{

    // Create data context for a specific area
    public IDataContext CreateDataContextForArea<TArea>() where TArea : IDatabaseArea
    {
        var areaType = typeof(TArea);

        // Try specific configuration first, then fallback to default
        DatabaseConnectionConfig? config = null;
        if (areaConfigurations.TryGetValue(areaType, out config))
        {
            // Use specific config
        }
        else if (defaultConfiguration != null)
        {
            config = defaultConfiguration;
        }
        else
        {
            throw new InvalidOperationException($"No database configuration found for area {areaType.Name} and no default configured.");
        }

        // Create concrete data context using the factory
        return config.Factory();
    }

    // Check if seeding is enabled for an area
    public bool IsSeedingEnabledForArea<TArea>() where TArea : IDatabaseArea
    {
        var areaType = typeof(TArea);

        DatabaseConnectionConfig? config;
        if (areaConfigurations.TryGetValue(areaType, out config))
        {
            return config?.SeedingOptions != null;
        }
        return defaultConfiguration?.SeedingOptions != null;
    }

    // Get seeding options for an area
    public DataSeedingOptions? GetSeedingOptionsForArea<TArea>() where TArea : IDatabaseArea
    {
        var areaType = typeof(TArea);

        DatabaseConnectionConfig? config;
        if (areaConfigurations.TryGetValue(areaType, out config))
        {
            return config?.SeedingOptions;
        }
        return defaultConfiguration?.SeedingOptions;
    }
}

// Builder implementation
public class DatabaseManagerBuilder : IDatabaseManagerBuilder, IDatabaseConnectionBuilder
{
    private readonly ConcurrentDictionary<Type, DatabaseConnectionConfig> _areaConfigurations = new();
    private DatabaseConnectionConfig? _defaultConfiguration;
    private DatabaseConnectionConfig? _currentConfig;

    // Set default configuration
    public IDatabaseManagerBuilder Default(Action<IDatabaseConnectionBuilder> configure)
    {
        _currentConfig = new DatabaseConnectionConfig();
        configure(this);
        _defaultConfiguration = _currentConfig;
        return this;
    }

    // Set configuration for specific area
    public IDatabaseManagerBuilder For<TArea>(Action<IDatabaseConnectionBuilder> configure) where TArea : IDatabaseArea
    {
        var areaType = typeof(TArea);
        _currentConfig = new DatabaseConnectionConfig();
        configure(this);
        _areaConfigurations[areaType] = _currentConfig;
        return this;
    }

    // Build the manager
    public IDatabaseManager Build()
    {
        return new DatabaseManager(_areaConfigurations, _defaultConfiguration);
    }

    // Implement SetFactory
    void IDatabaseConnectionBuilder.SetFactory(Func<IDataContext> factory)
    {
        if (_currentConfig != null)
            _currentConfig.Factory = factory;
    }

    // Implement EnableDataSeeding
    void IDatabaseConnectionBuilder.EnableDataSeeding(Action<DataSeedingOptions> configure)
    {
        if (_currentConfig != null)
        {
            _currentConfig.SeedingOptions = new DataSeedingOptions();
            configure(_currentConfig.SeedingOptions);
        }
    }
}
