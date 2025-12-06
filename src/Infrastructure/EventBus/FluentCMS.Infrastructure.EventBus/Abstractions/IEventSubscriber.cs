namespace FluentCMS.Infrastructure.EventBus.Abstractions;

/// <summary>
/// Generic event subscriber interface for handling domain events.
/// Implement this interface to create event handlers that respond to specific event types.
/// </summary>
/// <typeparam name="TEvent">The type of event this subscriber handles. Must implement <see cref="IEvent"/>.</typeparam>
/// <remarks>
/// Event subscribers are typically registered with dependency injection using the
/// <c>AddEventHandler</c> extension method. They will be invoked automatically when
/// matching events are published via <see cref="IEventPublisher"/>.
/// </remarks>
/// <example>
/// <code>
/// public class UserCreatedHandler : IEventSubscriber&lt;UserCreatedEvent&gt;
/// {
///     public async Task Handle(UserCreatedEvent domainEvent, CancellationToken cancellationToken)
///     {
///         // Handle the event (e.g., send welcome email)
///         await SendWelcomeEmail(domainEvent.UserId);
///     }
/// }
/// </code>
/// </example>
public interface IEventSubscriber<TEvent> where TEvent : class, IEvent
{
    /// <summary>
    /// Handles the specified domain event asynchronously.
    /// </summary>
    /// <param name="domainEvent">The event to handle. Cannot be null.</param>
    /// <param name="cancellationToken">
    /// A cancellation token that can be used to cancel the operation.
    /// Handlers should respect this token and stop processing if cancellation is requested.
    /// </param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <remarks>
    /// This method may be called concurrently with other handlers (in Aggregate mode)
    /// or sequentially (in FailFast mode), depending on the event publisher configuration.
    /// Ensure your implementation is thread-safe if it accesses shared resources.
    /// </remarks>
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken = default);
}
