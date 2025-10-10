namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Builds dependency graphs from plugin collections.
/// Analyzes assembly references to determine loading order.
/// </summary>
public class DependencyGraphBuilder
{
    /// <summary>
    /// Builds a dependency graph from a collection of plugins.
    /// </summary>
    /// <param name="plugins">The plugins to analyze for dependencies.</param>
    /// <returns>A dependency graph with the plugins sorted in loading order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when plugins is null.</exception>
    public DependencyGraph BuildGraph(IEnumerable<IPluginStartup> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var pluginList = plugins.ToList();
        var dependencies = BuildDependencyDictionary(pluginList);
        var (sortedPlugins, cycles) = PerformTopologicalSort(pluginList, dependencies);

        return new DependencyGraph(sortedPlugins.AsReadOnly(), dependencies, cycles.AsReadOnly());
    }

    /// <summary>
    /// Builds a dictionary mapping plugin names to their dependencies.
    /// Dependencies are determined by analyzing assembly references.
    /// </summary>
    /// <param name="plugins">The plugins to analyze.</param>
    /// <returns>A dictionary of plugin name to dependency list mappings.</returns>
    private static Dictionary<string, IReadOnlyList<string>> BuildDependencyDictionary(List<IPluginStartup> plugins)
    {
        var dependencies = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var plugin in plugins)
        {
            var pluginAssembly = plugin.GetType().Assembly;
            var pluginDependencies = new List<string>();

            // Analyze assembly references to find other plugin assemblies
            foreach (var reference in pluginAssembly.GetReferencedAssemblies())
            {
                var referencedPlugin = plugins.FirstOrDefault(p =>
                    p.GetType().Assembly.GetName().Name == reference.Name);

                if (referencedPlugin != null)
                {
                    pluginDependencies.Add(referencedPlugin.Name);
                }
            }

            dependencies[plugin.Name] = pluginDependencies.AsReadOnly();
        }

        return dependencies;
    }

    /// <summary>
    /// Performs topological sorting on the plugins based on their dependencies.
    /// Also detects and reports cycles in the dependency graph.
    /// </summary>
    /// <param name="plugins">The plugins to sort.</param>
    /// <param name="dependencies">The dependency relationships.</param>
    /// <returns>A tuple containing the sorted plugins and any detected cycles.</returns>
    private (List<IPluginStartup>, List<IReadOnlyList<string>>) PerformTopologicalSort(
        List<IPluginStartup> plugins,
        Dictionary<string, IReadOnlyList<string>> dependencies)
    {
        var sorted = new List<IPluginStartup>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        var cycles = new List<IReadOnlyList<string>>();

        var pluginByName = plugins.ToDictionary(p => p.Name);

        // Visit each plugin for topological sorting
        foreach (var plugin in plugins)
        {
            if (!visited.Contains(plugin.Name))
            {
                VisitPlugin(plugin.Name, pluginByName, dependencies, sorted, visited, visiting, cycles);
            }
        }

        // Reverse the list since we added dependencies first
        sorted.Reverse();

        return (sorted, cycles);
    }

    /// <summary>
    /// Recursively visits a plugin and its dependencies for topological sorting.
    /// Detects cycles using the standard DFS cycle detection algorithm.
    /// </summary>
    /// <param name="pluginName">The name of the plugin being visited.</param>
    /// <param name="pluginByName">Dictionary mapping plugin names to plugin instances.</param>
    /// <param name="dependencies">Dependency relationships.</param>
    /// <param name="sorted">The sorted list being built.</param>
    /// <param name="visited">Set of plugins already fully processed.</param>
    /// <param name="visiting">Set of plugins currently being processed (used for cycle detection).</param>
    /// <param name="cycles">List to collect any detected cycles.</param>
    private static void VisitPlugin(
        string pluginName,
        Dictionary<string, IPluginStartup> pluginByName,
        Dictionary<string, IReadOnlyList<string>> dependencies,
        List<IPluginStartup> sorted,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<IReadOnlyList<string>> cycles)
    {
        // If we're currently visiting this plugin, we found a cycle
        if (visiting.Contains(pluginName))
        {
            // Find the cycle path
            var cycle = new List<string>();
            var current = pluginName;

            // Build cycle path (this is a simplified cycle detection)
            // In a full implementation, you'd track the path more carefully
            do
            {
                cycle.Add(current);
                var deps = dependencies.TryGetValue(current, out var depsList) ? depsList : Array.Empty<string>();
                var next = deps.FirstOrDefault(d => visiting.Contains(d));
                if (next == null) break;
                current = next;
            } while (!cycle.Contains(current) && cycle.Count < visiting.Count + 1);

            cycles.Add(cycle.AsReadOnly());
            return;
        }

        // If already fully visited, skip
        if (visited.Contains(pluginName))
        {
            return;
        }

        // Mark as visiting
        visiting.Add(pluginName);

        // Visit all dependencies first
        if (dependencies.TryGetValue(pluginName, out var dependencyList))
        {
            foreach (var dependency in dependencyList)
            {
                if (pluginByName.ContainsKey(dependency))
                {
                    VisitPlugin(dependency, pluginByName, dependencies, sorted, visited, visiting, cycles);
                }
            }
        }

        // Mark as visited and add to sorted list
        visiting.Remove(pluginName);
        visited.Add(pluginName);

        if (pluginByName.TryGetValue(pluginName, out var plugin))
        {
            sorted.Add(plugin);
        }
    }
}
