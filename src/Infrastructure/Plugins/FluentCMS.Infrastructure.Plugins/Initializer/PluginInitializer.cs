namespace FluentCMS.Infrastructure.Plugins.Initializer;

internal interface IPluginInitializer
{
    PluginMetadata Initialize(Type pluginType);
}

internal class PluginInitializer(ILogger<PluginInitializer> logger, PluginSystemOptions pluginSystemOptions) : IPluginInitializer
{
    private readonly ILogger<PluginInitializer> _logger = NullArgumentException.RequireNonNull(logger);
    private readonly PluginSystemOptions _pluginSystemOptions = NullArgumentException.RequireNonNull(pluginSystemOptions);

    public PluginMetadata Initialize(Type pluginType)
    {
        try
        {
            NullArgumentException.ThrowIfNullOrEmpty(pluginType);
            if (!typeof(IPluginStartup).IsAssignableFrom(pluginType))
            {
                throw new ArgumentException($"Type {pluginType.FullName} does not implement IPluginStartup.");
            }
            // The plugin type should have a parameterless constructor
            var instance = (IPluginStartup)Activator.CreateInstance(pluginType)!;
            _logger.LogInformation("Initialized plugin {Plugin} version {Version}", instance.Name, instance.Version);

            return new PluginMetadata
            {
                Type = pluginType,
                Instance = instance,
                Name = instance.Name,
                Version = instance.Version,
                Status = PluginStatus.Initialized
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin initialization, but continuing due to IgnoreErrors setting");
            if (_pluginSystemOptions.IgnoreErrors)
            {
                _logger.LogWarning("Ignoring plugin initialization error due to configuration.");
                return new PluginMetadata
                {
                    Type = pluginType,
                    Status = PluginStatus.InitializeFailed,
                    ErrorMessage = ex.Message
                };
            }
            throw new PluginInitializerException("Error during plugin initialization.", ex);
        }
    }
}
