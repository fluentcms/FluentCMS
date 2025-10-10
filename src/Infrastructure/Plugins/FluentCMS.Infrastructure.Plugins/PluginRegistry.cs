using System.Collections.Concurrent;

namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Implements IPluginRegistry for tracking loaded plugins at runtime.
/// Provides thread-safe access to plugin information and status.
/// </summary>
public class PluginRegistry : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, PluginInfo> _plugins = new();

    /// <inheritdoc/>
    public IReadOnlyList<PluginInfo> GetAllPlugins() =>
        _plugins.Values.ToList().AsReadOnly();

    /// <inheritdoc/>
    public PluginInfo? GetPlugin(string pluginName) =>
        _plugins.TryGetValue(pluginName, out var plugin) ? plugin : null;

    /// <inheritdoc/>
    public bool IsPluginLoaded(string pluginName) =>
        _plugins.TryGetValue(pluginName, out var plugin) && plugin.Status == PluginStatus.Active;

    /// <inheritdoc/>
    public PluginStatus GetPluginStatus(string pluginName) =>
        _plugins.TryGetValue(pluginName, out var plugin) ? plugin.Status : PluginStatus.Unknown;

    /// <inheritdoc/>
    public IReadOnlyList<PluginInfo> GetActivePlugins() =>
        _plugins.Values.Where(p => p.Status == PluginStatus.Active).ToList().AsReadOnly();

    /// <inheritdoc/>
    public IReadOnlyList<PluginInfo> GetFailedPlugins() =>
        _plugins.Values.Where(p => p.Status is PluginStatus.Failed or PluginStatus.Failed).ToList().AsReadOnly();

    /// <inheritdoc/>
    public IReadOnlyList<PluginInfo> SearchPlugins(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return [];
        }

        var lowerTerm = searchTerm.ToLowerInvariant();
        return _plugins.Values
            .Where(p => p.Name.Contains(lowerTerm, StringComparison.InvariantCultureIgnoreCase) ||
                       p.Version?.Contains(lowerTerm, StringComparison.InvariantCultureIgnoreCase) == true)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Registers a plugin in the registry.
    /// </summary>
    /// <param name="pluginInfo">The plugin information to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when pluginInfo is null.</exception>
    public void RegisterPlugin(PluginInfo pluginInfo)
    {
        ArgumentNullException.ThrowIfNull(pluginInfo);

        _plugins[pluginInfo.Name] = pluginInfo;
    }

    /// <summary>
    /// Updates the status of a registered plugin.
    /// </summary>
    /// <param name="pluginName">The name of the plugin to update.</param>
    /// <param name="newStatus">The new status.</param>
    /// <param name="errorMessage">Optional error message for failed status.</param>
    /// <returns>True if the plugin was found and updated, false otherwise.</returns>
    public bool UpdatePluginStatus(string pluginName, PluginStatus newStatus, string? errorMessage = null)
    {
        if (!_plugins.TryGetValue(pluginName, out var existingPlugin))
        {
            return false;
        }

        var updatedPlugin = errorMessage != null
            ? existingPlugin.WithError(errorMessage)
            : existingPlugin.WithStatus(newStatus);

        _plugins[pluginName] = updatedPlugin;
        return true;
    }

    /// <summary>
    /// Removes a plugin from the registry.
    /// </summary>
    /// <param name="pluginName">The name of the plugin to remove.</param>
    /// <returns>True if the plugin was found and removed, false otherwise.</returns>
    public bool RemovePlugin(string pluginName) =>
        _plugins.TryRemove(pluginName, out _);

    /// <inheritdoc/>
    public int TotalPlugins => _plugins.Count;

    /// <inheritdoc/>
    public int SuccessfulPlugins => _plugins.Values.Count(p => p.Status == PluginStatus.Active);

    /// <inheritdoc/>
    public int FailedPlugins => _plugins.Values.Count(p => p.Status is PluginStatus.Failed or PluginStatus.Failed);

    /// <summary>
    /// Clears all plugins from the registry.
    /// </summary>
    public void Clear() =>
        _plugins.Clear();
}
