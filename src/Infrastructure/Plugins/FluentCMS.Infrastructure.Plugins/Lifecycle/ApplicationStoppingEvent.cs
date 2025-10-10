namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Event raised when the application is stopping.
/// </summary>
/// <remarks>
/// Initializes a new instance of the ApplicationStoppingEvent class.
/// </remarks>
/// <param name="stopReason">The reason for stopping.</param>
public sealed class ApplicationStoppingEvent(string? stopReason = null) : EventBase
{
    /// <summary>
    /// Gets the reason for stopping, if known.
    /// </summary>
    public string? StopReason { get; } = stopReason;
}
