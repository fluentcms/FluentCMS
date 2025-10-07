namespace FluentCMS.EventBus.InMemory;

/// <summary>
/// In-memory event publisher implementation
/// This is a simple implementation that invokes event handlers directly.
/// It is suitable for scenarios where low latency is required and
/// the number of event handlers is manageable.
/// It is registered as a singleton service to ensure a single instance
/// </summary>
internal class EventPublisher(IServiceScopeFactory scopeFactory, IOptions<EventPublisherOptions> options, ILogger<EventPublisher> logger) : IEventPublisher
{
    public async Task Publish<TEvent>(TEvent data, CancellationToken cancellationToken = default) where TEvent : class, IEvent
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(data);

        // Check for subscribers in the root provider first
        // TODO: we always create scope, find a solution to use root scoped provider if possible
        await using var scope = scopeFactory.CreateAsyncScope();
        var subscribers = scope.ServiceProvider.GetServices<IEventSubscriber<TEvent>>();

        if (!subscribers.Any())
        {
            // No subscribers found, log a warning and return
            logger.LogWarning("No subscribers found for event type {EventType}.", typeof(TEvent).Name);
            return;
        }


        if (options.Value.Mode == EventPublisherOptions.ErrorHandlingMode.FailFast)
        {
            // Execute handlers sequentially and stop on first exception
            foreach (var subscriber in subscribers)
            {
                try
                {
                    await subscriber.Handle(data, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while handling event of type {EventType} in subscriber {SubscriberType}. Event data: {@EventData}",
                        typeof(TEvent).Name,
                        subscriber.GetType().Name,
                        data);
                    throw;
                }
            }
        }
        else
        {
            // Execute all handlers concurrently and collect exceptions
            var exceptions = new ConcurrentBag<Exception>();

            var tasks = subscribers.Select(async subscriber =>
            {
                try
                {
                    await subscriber.Handle(data, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log detailed error information for each handler failure
                    logger.LogError(ex, "An error occurred while handling event of type {EventType} in subscriber {SubscriberType}. Event data: {@EventData}",
                        typeof(TEvent).Name,
                        subscriber.GetType().Name,
                        data);

                    // Collect exceptions but don't stop other handlers from executing
                    exceptions.Add(ex);
                }
            });

            // Wait for all handlers to complete
            await Task.WhenAll(tasks);

            // If any handlers threw exceptions, throw an aggregate exception
            if (!exceptions.IsEmpty)
            {
                throw new EventPublisherAggregatedException<TEvent>(exceptions);
            }
        }
    }
}
