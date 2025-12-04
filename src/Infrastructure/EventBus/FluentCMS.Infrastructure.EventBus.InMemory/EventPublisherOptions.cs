namespace FluentCMS.Infrastructure.EventBus.InMemory;

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
        /// Handlers execute SEQUENTIALLY in registration order.
        /// </summary>
        FailFast,

        /// <summary>
        /// Collect all exceptions and throw an aggregate exception at the end.
        /// Handlers execute CONCURRENTLY for better performance.
        /// Ensure handlers are thread-safe and don't depend on execution order.
        /// </summary>
        Aggregate
    }

    /// <summary>
    /// The error handling mode to use. Default is Aggregate.
    /// </summary>
    public ErrorHandlingMode Mode { get; set; } = ErrorHandlingMode.Aggregate;
}
