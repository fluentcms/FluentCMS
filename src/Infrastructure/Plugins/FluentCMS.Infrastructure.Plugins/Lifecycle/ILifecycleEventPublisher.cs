namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Interface for publishing lifecycle events throughout the plugin system.
/// Provides a centralized way to announce plugin lifecycle state changes.
/// </summary>
public interface ILifecycleEventPublisher
{
    /// <summary>
    /// Publishes an event to all interested subscribers.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to publish.</typeparam>
    /// <param name="event">The event instance to publish.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent;

    /// <summary>
    /// Publishes a plugin loading event.
    /// </summary>
    /// <param name="pluginName">The name of the plugin being loaded.</param>
    /// <param name="loadingPhase">The current loading phase.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishPluginLoading(string pluginName, PluginLoadingPhase loadingPhase, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a plugin loaded event.
    /// </summary>
    /// <param name="pluginName">The name of the plugin that was loaded.</param>
    /// <param name="pluginVersion">The version of the plugin.</param>
    /// <param name="dependencies">The plugin's dependencies.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishPluginLoaded(string pluginName, string pluginVersion, IReadOnlyList<string> dependencies, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a plugin failed event.
    /// </summary>
    /// <param name="pluginName">The name of the plugin that failed.</param>
    /// <param name="errorMessage">The error message describing the failure.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishPluginFailed(string pluginName, string errorMessage, Exception? exception, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an application started event.
    /// </summary>
    /// <param name="pluginCount">The number of plugins loaded.</param>
    /// <param name="startupTime">How long the application took to start.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishApplicationStarted(int pluginCount, TimeSpan startupTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an application stopping event.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishApplicationStopping(CancellationToken cancellationToken = default);
}
