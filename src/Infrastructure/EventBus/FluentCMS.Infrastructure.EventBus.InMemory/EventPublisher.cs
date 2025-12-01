namespace FluentCMS.Infrastructure.EventBus.InMemory;

/// <summary>
/// In-memory event publisher implementation
/// This is a simple implementation that invokes event handlers directly.
/// It is suitable for scenarios where low latency is required and
/// the number of event handlers is manageable.
/// It is registered as a singleton service to ensure a single instance
/// </summary>
internal class EventPublisher(IServiceScopeFactory scopeFactory, IOptions<EventPublisherOptions> options, ILogger<EventPublisher> logger, IHttpContextAccessor? httpContextAccessor = null) : IEventPublisher
{
    public async Task Publish<TEvent>(TEvent data, CancellationToken cancellationToken = default) where TEvent : class, IEvent
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(data);

        var httpContext = httpContextAccessor?.HttpContext;
        IServiceProvider provider;
        AsyncServiceScope? scopeToDispose = null;

        // Safe scope creation and assignment - both happen inside try block to ensure proper disposal
        try
        {
            if (httpContext != null)
            {
                // We're in an HTTP request - use the request's service provider
                provider = httpContext.RequestServices;
                logger.LogDebug("Using HTTP request scope for event {EventType}", typeof(TEvent).Name);
            }
            else
            {
                // No HTTP context - create new scope
                // Assignment happens in try block to ensure disposal on exception
                scopeToDispose = scopeFactory.CreateAsyncScope();
                provider = scopeToDispose.Value.ServiceProvider;
                logger.LogDebug("Created new scope for event {EventType}", typeof(TEvent).Name);
            }

            var subscribers = provider.GetServices<IEventSubscriber<TEvent>>();

            if (!subscribers.Any())
            {
                // No subscribers found, log a warning and return
                logger.LogWarning("No subscribers found for event type {EventType}.", typeof(TEvent).Name);
                return;
            }

            if (options.Value.Mode == EventPublisherOptions.ErrorHandlingMode.FailFast)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // SEQUENTIAL execution: handlers run one at a time in registration order
                // Stops on first exception
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
                // CONCURRENT execution: all handlers run in parallel via Task.WhenAll
                // Collects all exceptions and throws aggregate at the end
                var exceptions = new ConcurrentBag<Exception>();

                var tasks = subscribers.Select(async subscriber =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
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

                cancellationToken.ThrowIfCancellationRequested();
                // Wait for all handlers to complete
                await Task.WhenAll(tasks);

                // If any handlers threw exceptions, throw an aggregate exception
                if (!exceptions.IsEmpty)
                {
                    throw new EventPublisherAggregatedException<TEvent>(exceptions);
                }
            }

            // Log successful event publishing
            logger.LogInformation("Event of type {EventType} published successfully to {SubscriberCount} subscriber(s).", typeof(TEvent).Name, subscribers.Count());
        }
        finally
        {
            // Safe disposal - only disposes if scope was successfully created and assigned
            if (scopeToDispose.HasValue)
            {
                await scopeToDispose.Value.DisposeAsync();
                logger.LogDebug("Disposed scope for event {EventType}", typeof(TEvent).Name);
            }
        }
    }
}
