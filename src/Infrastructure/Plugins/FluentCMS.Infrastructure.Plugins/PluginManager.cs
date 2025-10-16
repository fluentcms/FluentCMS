namespace FluentCMS.Infrastructure.Plugins;

internal interface IPluginManager
{
    void Configure(IServiceCollection services, IConfiguration rootConfiguration, CancellationToken cancellationToken);
    void Start(IApplicationBuilder app, CancellationToken cancellationToken);
}

internal class PluginManager(IPluginDiscovery pluginDiscovery, IPluginInitializer pluginInitializer, IPluginLoader pluginLoader, ILogger<PluginManager> logger, PluginSystemOptions pluginSystemOptions) : IPluginManager
{
    private readonly ILogger<PluginManager> _logger = NullArgumentException.RequireNonNull(logger);
    private readonly PluginSystemOptions _pluginSystemOptions = NullArgumentException.RequireNonNull(pluginSystemOptions);
    private readonly List<PluginMetadata> _pluginMetadataList = [];

    private void Init(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting plugin initialization process...");
        var assemblyFiles = pluginDiscovery.Scan(cancellationToken);
        var pluginTypes = pluginLoader.LoadPluginTypes(assemblyFiles, cancellationToken);

        foreach (var pluginType in pluginTypes)
        {
            var metadata = pluginInitializer.Initialize(pluginType);
            _pluginMetadataList.Add(metadata);
        }
        _logger.LogInformation("PluginManager initialized with {Count} plugins", _pluginMetadataList.Count);
    }

    public void Configure(IServiceCollection services, IConfiguration rootConfiguration, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting plugin configuration process...");
        Init(cancellationToken);

        // run configure services for each plugin in order
        foreach (var pluginMetadata in _pluginMetadataList.Where(p => p.Status == PluginStatus.Initialized).OrderBy(p => p.Instance!.ConfigureServicesPriority))
        {
            try
            {
                pluginMetadata.Instance!.ConfigureServices(services, rootConfiguration);
                pluginMetadata.Status = PluginStatus.Configured;
                _logger.LogInformation("Configured services for plugin {Plugin} version {Version}", pluginMetadata.Name, pluginMetadata.Version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ConfigureServices of plugin {Plugin}, but continuing due to IgnoreErrors setting", pluginMetadata.Name);
                if (_pluginSystemOptions.IgnoreErrors)
                {
                    _logger.LogWarning("Ignoring ConfigureServices error for plugin {Plugin} due to configuration.", pluginMetadata.Name);
                    pluginMetadata.ErrorMessage = ex.Message;
                    pluginMetadata.Status = PluginStatus.ConfigurationFailed;
                    continue;
                }
                throw;
            }
        }
        _logger.LogInformation("Plugin configuration process completed. {Count} plugins loaded.", _pluginMetadataList.Count(p => p.Status == PluginStatus.Configured));
        var failedCount = _pluginMetadataList.Count(p => p.Status == PluginStatus.ConfigurationFailed || p.Status == PluginStatus.InitializeFailed || p.Status == PluginStatus.NotInitialized);
        if (failedCount > 0)
            _logger.LogWarning("Plugins configuration process with errors: {Count}", failedCount);
    }

    public void Start(IApplicationBuilder app, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting plugin startup process...");
        foreach (var pluginMetadata in _pluginMetadataList.Where(p => p.Status == PluginStatus.Configured).OrderBy(p => p.Instance!.ConfigurePriority))
        {
            try
            {
                pluginMetadata.Instance!.Configure(app);
                pluginMetadata.Status = PluginStatus.Started;
                _logger.LogInformation("Startup plugin {Plugin} version {Version}", pluginMetadata.Name, pluginMetadata.Version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during startup of plugin {Plugin}, but continuing due to IgnoreErrors setting", pluginMetadata.Name);
                if (_pluginSystemOptions.IgnoreErrors)
                {
                    _logger.LogWarning("Ignoring startup error for plugin {Plugin} due to configuration.", pluginMetadata.Name);
                    pluginMetadata.ErrorMessage = ex.Message;
                    pluginMetadata.Status = PluginStatus.StartFailed;
                    continue;
                }

                throw;
            }
        }
        _logger.LogInformation("Plugin startup process completed. {Count} plugins started.", _pluginMetadataList.Count(p => p.Status == PluginStatus.Started));
        var failedCount = _pluginMetadataList.Count(p => p.Status != PluginStatus.Started);
        if (failedCount > 0)
            _logger.LogWarning("Plugins startup process with errors: {Count}", failedCount);
    }
}
