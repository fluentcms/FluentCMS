namespace FluentCMS.EventBus.InMemory;

/// <summary>
/// Options for configuring the event publisher.
/// </summary>
public class EventPublisherOptions
{
    /// <summary>
    /// Defines how exceptions from event handlers are handled.
    /// </summary>
    public enum ErrorHandlingMode
    {
        /// <summary>
        /// Throw exception immediately on the first failed handler.
        /// </summary>
        FailFast,

        /// <summary>
        /// Collect all exceptions and throw an aggregate exception at the end.
        /// </summary>
        Aggregate
    }

    /// <summary>
    /// The error handling mode to use. Default is Aggregate.
    /// </summary>
    public ErrorHandlingMode Mode { get; set; } = ErrorHandlingMode.Aggregate;
}
