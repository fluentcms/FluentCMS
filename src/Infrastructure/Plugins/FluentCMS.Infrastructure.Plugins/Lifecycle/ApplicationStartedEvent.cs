namespace FluentCMS.Infrastructure.Plugins.Lifecycle;

/// <summary>
/// Event fired when the application has started and all plugins are loaded.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ApplicationStartedEvent"/> class.
/// </remarks>
/// <param name="totalPlugins">The total number of plugins loaded.</param>
/// <param name="loadedPlugins">The names of successfully loaded plugins.</param>
/// <param name="failedPlugins">The names of plugins that failed to load.</param>
public sealed class ApplicationStartedEvent(
    int totalPlugins,
    IReadOnlyList<string> loadedPlugins,
    IReadOnlyList<string> failedPlugins) : EventBase
{

    /// <summary>
    /// Gets the total number of plugins discovered (loaded + failed).
    /// </summary>
    public int TotalPlugins { get; } = totalPlugins;

    /// <summary>
    /// Gets the names of plugins that were successfully loaded.
    /// </summary>
    public IReadOnlyList<string> LoadedPlugins { get; } = loadedPlugins ?? throw new ArgumentNullException(nameof(loadedPlugins));

    /// <summary>
    /// Gets the names of plugins that failed to load.
    /// </summary>
    public IReadOnlyList<string> FailedPlugins { get; } = failedPlugins ?? throw new ArgumentNullException(nameof(failedPlugins));
}
