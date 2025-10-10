namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Severity levels for resource quota violations.
/// </summary>
public enum QuotaViolationSeverity
{
    /// <summary>
    /// Warning level - monitor closely but no immediate action needed.
    /// </summary>
    Warning,

    /// <summary>
    /// Moderate level - may impact performance, consider action.
    /// </summary>
    Moderate,

    /// <summary>
    /// Critical level - requires attention and potential throttling.
    /// </summary>
    Critical,

    /// <summary>
    /// Severe level - plugin should be stopped or restarted.
    /// </summary>
    Severe
}
