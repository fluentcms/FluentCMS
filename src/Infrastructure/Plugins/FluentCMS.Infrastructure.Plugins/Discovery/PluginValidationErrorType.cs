namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Defines the types of plugin validation errors.
/// </summary>
public enum PluginValidationErrorType
{
    /// <summary>
    /// Duplicate plugin names were found.
    /// </summary>
    DuplicatePluginNames,

    /// <summary>
    /// A plugin dependency does not exist.
    /// </summary>
    MissingDependency,

    /// <summary>
    /// A circular dependency was detected.
    /// </summary>
    CircularDependency,

    /// <summary>
    /// Invalid priority value detected.
    /// </summary>
    InvalidPriority,

    /// <summary>
    /// Plugin assembly could not be validated.
    /// </summary>
    InvalidAssembly,

    /// <summary>
    /// General validation failure.
    /// </summary>
    General
}
