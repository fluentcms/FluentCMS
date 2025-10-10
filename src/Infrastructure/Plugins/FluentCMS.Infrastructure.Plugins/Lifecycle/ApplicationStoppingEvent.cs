namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Event fired when the application is stopping.
/// </summary>
public sealed class ApplicationStoppingEvent : EventBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationStoppingEvent"/> class.
    /// </summary>
    public ApplicationStoppingEvent()
    {
    }
}
