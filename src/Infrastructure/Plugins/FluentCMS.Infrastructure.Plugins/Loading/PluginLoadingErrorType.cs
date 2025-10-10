namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Defines the types of plugin loading errors.
/// </summary>
public enum PluginLoadingErrorType
{
    /// <summary>
    /// Error during plugin discovery phase.
    /// </summary>
    DiscoveryFailure,

    /// <summary>
    /// Validation errors found in plugins.
    /// </summary>
    ValidationFailure,

    /// <summary>
    /// Error during service registration phase.
    /// </summary>
    ServiceRegistrationFailure,

    /// <summary>
    /// Error during pipeline configuration phase.
    /// </summary>
    PipelineConfigurationFailure,

    /// <summary>
    /// Timeout occurred during loading.
    /// </summary>
    Timeout,

    /// <summary>
    /// General loading failure.
    /// </summary>
    GeneralFailure
}
