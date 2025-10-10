namespace FluentCMS.Infrastructure.Plugins.Abstractions;

/// <summary>
/// Interface for managing and querying loaded plugins in the system.
/// Provides access to plugin metadata and runtime information.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>
    /// Gets all loaded plugins.
    /// </summary>
    /// <returns>A read-only list of information about all loaded plugins.</returns>
    IReadOnlyList<PluginInfo> GetAllPlugins();

    /// <summary>
    /// Gets information about a specific plugin by name.
    /// </summary>
    /// <param name="pluginName">The name of the plugin to retrieve.</param>
    /// <returns>The plugin information, or null if not found.</returns>
    PluginInfo? GetPlugin(string pluginName);

    /// <summary>
    /// Checks if a plugin with the specified name is currently loaded.
    /// </summary>
    /// <param name="pluginName">The name of the plugin to check.</param>
    /// <returns>true if the plugin is loaded; otherwise, false.</returns>
    bool IsPluginLoaded(string pluginName);

    /// <summary>
    /// Gets the current status of a specific plugin.
    /// </summary>
    /// <param name="pluginName">The name of the plugin to check.</param>
    /// <returns>The plugin's current status.</returns>
    /// <exception cref="ArgumentException">Thrown when the plugin is not found.</exception>
    PluginStatus GetPluginStatus(string pluginName);

    /// <summary>
    /// Gets all plugins that are currently in an active state.
    /// </summary>
    /// <returns>A list of active plugins.</returns>
    IReadOnlyList<PluginInfo> GetActivePlugins();

    /// <summary>
    /// Gets all plugins that failed to load.
    /// </summary>
    /// <returns>A list of plugins that failed loading with their error information.</returns>
    IReadOnlyList<PluginInfo> GetFailedPlugins();

    /// <summary>
    /// Searches for plugins by name using a case-insensitive partial match.
    /// </summary>
    /// <param name="searchTerm">The search term to match against plugin names.</param>
    /// <returns>A list of plugins whose names contain the search term.</returns>
    IReadOnlyList<PluginInfo> SearchPlugins(string searchTerm);

    /// <summary>
    /// Gets the total number of loaded plugins.
    /// </summary>
    int TotalPlugins { get; }

    /// <summary>
    /// Gets the number of successfully loaded plugins.
    /// </summary>
    int SuccessfulPlugins { get; }

    /// <summary>
    /// Gets the number of plugins that failed to load.
    /// </summary>
    int FailedPlugins { get; }
}
