namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Result of plugin validation with detailed error information.
/// </summary>
public class PluginValidationResult
{
    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the list of validation errors. Empty if validation passed.
    /// </summary>
    public IReadOnlyList<PluginValidationError> Errors { get; }

    /// <summary>
    /// Gets the dependency graph if validation passed and graph is available.
    /// </summary>
    public DependencyGraph? DependencyGraph { get; }

    /// <summary>
    /// Initializes a new instance of PluginValidationResult for successful validation.
    /// </summary>
    /// <param name="dependencyGraph">The validated dependency graph.</param>
    public PluginValidationResult(DependencyGraph dependencyGraph)
    {
        IsValid = true;
        DependencyGraph = dependencyGraph ?? throw new ArgumentNullException(nameof(dependencyGraph));
        Errors = [];
    }

    ///// <summary>
    ///// Exception thrown when plugin validation fails.
    ///// </summary>
    //public class PluginValidationException : Exception
    //{
    //    /// <summary>
    //    /// Gets the validation errors that caused this exception.
    //    /// </summary>
    //    public IReadOnlyList<PluginValidationError> ValidationErrors { get; }

    //    /// <summary>
    //    /// Initializes a new instance of the PluginValidationException class.
    //    /// </summary>
    //    /// <param name="validationResult">The validation result containing the errors.</param>
    //    public PluginValidationException(PluginValidationResult validationResult)
    //        : base($"Plugin validation failed with {validationResult.Errors.Count} error(s): {string.Join("; ", validationResult.Errors.Select(e => e.Message))}")
    //    {
    //        ValidationErrors = validationResult.Errors;
    //    }

    //    /// <summary>
    //    /// Initializes a new instance of the PluginValidationException class.
    //    /// </summary>
    //    /// <param name="message">The error message.</param>
    //    public PluginValidationException(string message) : base(message)
    //    {
    //        ValidationErrors = [];
    //    }

    //    /// <summary>
    //    /// Initializes a new instance of the PluginValidationException class.
    //    /// </summary>
    //    /// <param name="message">The error message.</param>
    //    /// <param name="innerException">The inner exception.</param>
    //    public PluginValidationException(string message, Exception innerException) : base(message, innerException)
    //    {
    //        ValidationErrors = [];
    //    }
    //}

    /// <summary>
    /// Initializes a new instance of PluginValidationResult for failed validation.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    public PluginValidationResult(IEnumerable<PluginValidationError> errors)
    {
        IsValid = false;
        Errors = [.. (errors ?? throw new ArgumentNullException(nameof(errors)))];
        DependencyGraph = null;
    }
}
