namespace FluentCMS.Infrastructure.Plugins;

public class PluginMetadata
{
    public string? Name { get; internal set; }
    public string? Version { get; internal set; }
    public Type Type { get; init; } = default!;
    public IPluginStartup? Instance { get; internal set; }
    public PluginStatus Status { get; internal set; }
    public string? ErrorMessage { get; internal set; }
}

public enum PluginStatus
{
    NotInitialized,
    InitializeFailed,
    Initialized,
    Configured,
    ConfigurationFailed,
    Started,
    StartFailed,
}
