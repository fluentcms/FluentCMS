namespace FluentCMS.EventBus.InMemory;

// IMPORTANT: This class is typically registered as Singleton.
// Ensure thread-safety for any mutable state accessed in HandleAsync.
public abstract class EventHandlerBase<TEvent> : IEventSubscriber<TEvent>
    where TEvent : class, IEvent
{
    private readonly ILogger _logger;

    protected EventHandlerBase(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger(GetType());
    }

    public async Task Handle(TEvent domainEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            await HandleAsync(domainEvent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Event handle canceled for {EventType}", typeof(TEvent).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {EventType}", typeof(TEvent).Name);
            throw;
        }
    }

    protected abstract Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
