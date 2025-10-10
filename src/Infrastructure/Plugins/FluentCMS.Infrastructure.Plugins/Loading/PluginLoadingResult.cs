namespace FluentCMS.Infrastructure.Plugins.Loading;

/// <summary>
/// Result of the plugin loading process.
/// </summary>
/// <remarks>
/// Initializes a new instance of PluginLoadingResult.
/// </remarks>
/// <param name="loadedPlugins">The plugins that were successfully loaded.</param>
/// <param name="errors">Any errors that occurred during loading.</param>
/// <param name="loadingDuration">How long the loading process took.</param>
/// <param name="completedAt">When loading was completed.</param>
public class PluginLoadingResult(IReadOnlyList<PluginInfo> loadedPlugins, IReadOnlyList<PluginLoadingError> errors, TimeSpan loadingDuration, DateTimeOffset completedAt)
{
    /// <summary>
    /// Gets whether the loading process was successful.
    /// </summary>
    public bool IsSuccessful => Errors.Count == 0;

    /// <summary>
    /// Gets the list of plugins that were successfully loaded.
    /// </summary>
    public IReadOnlyList<PluginInfo> LoadedPlugins { get; } = NullArgumentException.RequireNonNull(loadedPlugins);

    /// <summary>
    /// Gets the list of validation or loading errors that occurred.
    /// </summary>
    public IReadOnlyList<PluginLoadingError> Errors { get; }  = NullArgumentException.RequireNonNull(errors);

    /// <summary>
    /// Gets the duration of the loading process.
    /// </summary>
    public TimeSpan LoadingDuration { get; } = loadingDuration;

    /// <summary>
    /// Gets the timestamp when loading completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; } = completedAt;
}
