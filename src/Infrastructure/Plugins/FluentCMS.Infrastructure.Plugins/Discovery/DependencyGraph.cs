namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Represents a dependency graph of plugins with topological sorting capabilities.
/// Ensures plugins are loaded in the correct dependency order.
/// </summary>
/// <remarks>
/// Initializes a new instance of the DependencyGraph class.
/// </remarks>
/// <param name="sortedPlugins">Plugins sorted in topological order.</param>
/// <param name="dependencies">Dependency relationships between plugins.</param>
/// <param name="cycles">Any cycles detected in the graph.</param>
public class DependencyGraph(IReadOnlyList<IPluginStartup> sortedPlugins, IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies, IReadOnlyList<IReadOnlyList<string>> cycles)
{
    /// <summary>
    /// Gets the plugins sorted in topological order (dependencies first).
    /// </summary>
    public IReadOnlyList<IPluginStartup> SortedPlugins { get; } = NullArgumentException.RequireNonNull(sortedPlugins);

    /// <summary>
    /// Gets the dependency relationships between plugins.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies { get; } = NullArgumentException.RequireNonNull(dependencies);

    /// <summary>
    /// Gets any cycles detected in the dependency graph.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Cycles { get; } = NullArgumentException.RequireNonNull(cycles);

    /// <summary>
    /// Gets whether the dependency graph is valid (no cycles).
    /// </summary>
    public bool IsValid => Cycles.Count == 0;

    /// <summary>
    /// Gets the dependencies for a specific plugin.
    /// </summary>
    /// <param name="pluginName">The name of the plugin.</param>
    /// <returns>A read-only list of dependency names.</returns>
    public IReadOnlyList<string> GetDependencies(string pluginName)
    {
        return Dependencies.TryGetValue(pluginName ?? string.Empty, out var deps) ? deps : Array.Empty<string>();
    }

    /// <summary>
    /// Determines if a plugin has any dependencies.
    /// </summary>
    /// <param name="pluginName">The name of the plugin.</param>
    /// <returns>true if the plugin has dependencies, false otherwise.</returns>
    public bool HasDependencies(string pluginName)
    {
        return Dependencies.TryGetValue(pluginName ?? string.Empty, out var deps) && deps.Count > 0;
    }

    /// <summary>
    /// Gets all plugins that depend on the specified plugin.
    /// </summary>
    /// <param name="pluginName">The name of the plugin to check.</param>
    /// <returns>A list of plugins that depend on the specified plugin.</returns>
    public IReadOnlyList<string> GetDependents(string pluginName)
    {
        if (string.IsNullOrEmpty(pluginName))
        {
            return [];
        }

        var dependents = new List<string>();
        foreach (var (dependentName, deps) in Dependencies)
        {
            if (deps.Contains(pluginName))
            {
                dependents.Add(dependentName);
            }
        }
        return dependents;
    }
}
