namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Implementation of ILifecycleEventPublisher that uses IEventPublisher for event publishing.
/// Provides both generic and specific event publishing methods for plugin lifecycle events.
/// </summary>
/// <remarks>
/// Initializes a new instance of the LifecycleEventPublisher class.
/// </remarks>
/// <param name="publisher">The IEventPublisher for event publishing.</param>
/// <param name="logger">The logger for recording event publishing activities.</param>
public class LifecycleEventPublisher(IEventPublisher publisher, ILogger<LifecycleEventPublisher> logger) : ILifecycleEventPublisher
{
    /// <inheritdoc/>
    public async Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        try
        {
            logger.LogDebug("Publishing {EventType} event {EventId}", typeof(TEvent).Name, @event.EventId);

            await publisher.Publish(@event, cancellationToken);

            logger.LogDebug("Successfully published {EventType} event {EventId}", typeof(TEvent).Name, @event.EventId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish {EventType} event {EventId}: {Message}", typeof(TEvent).Name, @event.EventId, ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task PublishPluginLoading(string pluginName, PluginLoadingPhase loadingPhase, CancellationToken cancellationToken = default)
    {
        NullArgumentException.ThrowIfNullOrEmpty(pluginName);

        var @event = new PluginLoadingEvent(
            pluginName,
            loadingPhase,
            new Dictionary<string, object>
            {
                ["Timestamp"] = DateTimeOffset.Now,
                ["PhaseDescription"] = loadingPhase.ToString()
            });

        await Publish(@event, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task PublishPluginLoaded(string pluginName, string pluginVersion, IReadOnlyList<string> dependencies, CancellationToken cancellationToken = default)
    {
        NullArgumentException.ThrowIfNullOrEmpty(pluginName);
        NullArgumentException.ThrowIfNullOrEmpty(pluginVersion);

        ArgumentNullException.ThrowIfNull(dependencies);

        var @event = new PluginLoadedEvent(
            pluginName,
            pluginVersion,
            dependencies,
            DateTimeOffset.Now);

        await Publish(@event, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task PublishPluginFailed(string pluginName, string errorMessage, Exception? exception, CancellationToken cancellationToken = default)
    {
        NullArgumentException.ThrowIfNullOrEmpty(pluginName);
        NullArgumentException.ThrowIfNullOrEmpty(errorMessage);

        var @event = new PluginFailedEvent(
            pluginName,
            errorMessage,
            exception,
            PluginLoadingPhase.Discovery); // Default to discovery phase

        await Publish(@event, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task PublishApplicationStarted(int pluginCount, TimeSpan startupTime, CancellationToken cancellationToken = default)
    {
        var @event = new ApplicationStartedEvent(
            pluginCount,
            true, // Assume success for now - this could be enhanced
            startupTime,
            [],  // Could be populated with actual successfulley loaded
            []); // Could be populated with actual failed plugins

        await Publish(@event, cancellationToken);
    }

    public async Task PublishApplicationStopping(CancellationToken cancellationToken = default)
    {
        var @event = new ApplicationStoppingEvent("Application shutdown requested");
        await Publish(@event, cancellationToken);
    }
}
