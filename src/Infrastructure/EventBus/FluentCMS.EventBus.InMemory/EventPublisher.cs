namespace FluentCMS.EventBus.InMemory;

public class EventPublisher(IServiceProvider serviceProvider) : IEventPublisher
{
    // Cache the generic publish method to avoid reflection lookup on each call
    private static readonly MethodInfo _publishGenericMethod =
        typeof(EventPublisher).GetMethod(nameof(Publish))!;

    private readonly EventPublisherOptions _options = serviceProvider.GetService<IOptions<EventPublisherOptions>>()?.Value ??
        throw new ArgumentNullException(nameof(EventPublisherOptions));

    protected readonly IServiceProvider ServiceProvider = serviceProvider ??
        throw new ArgumentNullException(nameof(serviceProvider));

    private readonly ILogger<EventPublisher> _logger = serviceProvider.GetService<ILogger<EventPublisher>>() ??
        throw new ArgumentNullException(nameof(_logger));

    public async Task Publish<TEvent>(TEvent data, CancellationToken cancellationToken = default) where TEvent : class, IEvent
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(data);

        // Get all registered subscribers for this event type
        var subscribers = ServiceProvider.GetServices<IEventSubscriber<TEvent>>().ToList();

        if (subscribers.Count == 0)
        {
            // No subscribers found, log a warning and return
            _logger.LogWarning("No subscribers found for event type {EventType}.", typeof(TEvent).Name);
            return;
        }


        if (_options.Mode == EventPublisherOptions.ErrorHandlingMode.FailFast)
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
                    _logger.LogError(ex, "An error occurred while handling event of type {EventType} in subscriber {SubscriberType}. Event data: {@EventData}",
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
                    _logger.LogError(ex, "An error occurred while handling event of type {EventType} in subscriber {SubscriberType}. Event data: {@EventData}",
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

    public Task Publish(object eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // Ensure the event implements IEvent
        if (eventData is not IEvent eventObj)
        {
            _logger.LogError("Event data must implement {InterfaceName}", nameof(IEvent));
            throw new ArgumentException($"Event data must implement {nameof(IEvent)}", nameof(eventData));
        }

        // Get the actual event type at runtime
        var eventType = eventData.GetType();

        // Use cached generic method to make invocation more efficient
        var publishMethod = _publishGenericMethod.MakeGenericMethod(eventType);

        // Invoke: Publish<ActualEventType>(eventData, cancellationToken)
        return (Task)publishMethod.Invoke(this, [eventData, cancellationToken])!;

    }
}
