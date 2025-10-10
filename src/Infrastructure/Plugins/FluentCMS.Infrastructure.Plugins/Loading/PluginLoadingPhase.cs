namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Defines the phases of the plugin loading process.
/// </summary>
public enum PluginLoadingPhase
{
    /// <summary>
    /// Discovery phase: Scanning assemblies and finding plugins.
    /// </summary>
    Discovery,

    /// <summary>
    /// Validation phase: Validating plugin dependencies and configurations.
    /// </summary>
    Validation,

    /// <summary>
    /// Service Registration phase: Registering services in DI container.
    /// </summary>
    ServiceRegistration,

    /// <summary>
    /// Pipeline Configuration phase: Configuring middleware pipeline.
    /// </summary>
    PipelineConfiguration
}
