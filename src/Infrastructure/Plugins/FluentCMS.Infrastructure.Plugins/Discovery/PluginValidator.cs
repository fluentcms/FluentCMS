namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Validates plugin collections to ensure they can be loaded safely.
/// Performs comprehensive checks for dependency resolution, circular references, and configuration validity.
/// </summary>
/// <remarks>
/// Initializes a new instance of the PluginValidator class.
/// </remarks>
/// <param name="dependencyGraphBuilder">The dependency graph builder to use.</param>
public class PluginValidator(DependencyGraphBuilder dependencyGraphBuilder)
{
    private readonly DependencyGraphBuilder _dependencyGraphBuilder = dependencyGraphBuilder ?? throw new ArgumentNullException(nameof(dependencyGraphBuilder));

    /// <summary>
    /// Validates a collection of plugins and returns detailed validation results.
    /// </summary>
    /// <param name="plugins">The plugins to validate.</param>
    /// <returns>A validation result containing either a validated dependency graph or error details.</returns>
    public PluginValidationResult ValidatePlugins(IEnumerable<IPluginStartup> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var pluginList = plugins.ToList();
        var errors = new List<PluginValidationError>();

        // Validate plugin uniqueness
        errors.AddRange(ValidatePluginUniqueness(pluginList));

        // Early exit if we have basic validation errors
        if (errors.Count != 0)
        {
            return new PluginValidationResult(errors);
        }

        // Build dependency graph and check for cycles
        try
        {
            var dependencyGraph = _dependencyGraphBuilder.BuildGraph(pluginList);

            // Validate circular dependencies
            errors.AddRange(ValidateCircularDependencies(dependencyGraph));

            // Validate dependencies exist
            errors.AddRange(ValidateDependenciesExist(dependencyGraph, pluginList));

            // Validate priority values
            errors.AddRange(ValidatePriorities(pluginList));

            // If we have any errors, return them
            if (errors.Any())
            {
                return new PluginValidationResult(errors);
            }

            // Success - return the validated dependency graph
            return new PluginValidationResult(dependencyGraph);
        }
        catch (Exception ex)
        {
            errors.Add(new PluginValidationError(
                PluginValidationErrorType.General,
                null,
                $"Failed to build dependency graph: {ex.Message}",
                new Dictionary<string, object> { ["ExceptionType"] = ex.GetType().Name }));

            return new PluginValidationResult(errors);
        }
    }

    /// <summary>
    /// Validates that all plugin names are unique.
    /// </summary>
    /// <param name="plugins">The plugins to validate.</param>
    /// <returns>A list of validation errors for duplicate names.</returns>
    private static List<PluginValidationError> ValidatePluginUniqueness(List<IPluginStartup> plugins)
    {
        var nameCount = new Dictionary<string, List<IPluginStartup>>();
        var errors = new List<PluginValidationError>();

        foreach (var plugin in plugins)
        {
            if (!nameCount.TryGetValue(plugin.Name, out var pluginWithName))
            {
                pluginWithName = [];
                nameCount[plugin.Name] = pluginWithName;
            }
            pluginWithName.Add(plugin);
        }

        foreach (var (name, pluginsWithName) in nameCount)
        {
            if (pluginsWithName.Count > 1)
            {
                errors.Add(new PluginValidationError(
                    PluginValidationErrorType.DuplicatePluginNames,
                    name,
                    $"Multiple plugins found with the same name '{name}'",
                    new Dictionary<string, object>
                    {
                        ["PluginTypeNames"] = string.Join(", ", pluginsWithName.Select(p => p.GetType().FullName)),
                        ["Count"] = pluginsWithName.Count
                    }));
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates that there are no circular dependencies in the dependency graph.
    /// </summary>
    /// <param name="dependencyGraph">The dependency graph to validate.</param>
    /// <returns>A list of validation errors for circular dependencies.</returns>
    private static List<PluginValidationError> ValidateCircularDependencies(DependencyGraph dependencyGraph)
    {
        var errors = new List<PluginValidationError>();

        foreach (var cycle in dependencyGraph.Cycles)
        {
            var cycleNames = cycle.ToList();
            errors.Add(new PluginValidationError(
                PluginValidationErrorType.CircularDependency,
                cycleNames.First(),
                $"Circular dependency detected: {string.Join(" -> ", cycleNames)} -> {cycleNames.First()}",
                new Dictionary<string, object>
                {
                    ["Cycle"] = cycleNames,
                    ["CycleLength"] = cycleNames.Count
                }));
        }

        return errors;
    }

    /// <summary>
    /// Validates that all plugin dependencies actually exist in the plugin set.
    /// </summary>
    /// <param name="dependencyGraph">The dependency graph to validate.</param>
    /// <param name="plugins">The complete set of available plugins.</param>
    /// <returns>A list of validation errors for missing dependencies.</returns>
    private static List<PluginValidationError> ValidateDependenciesExist(DependencyGraph dependencyGraph, List<IPluginStartup> plugins)
    {
        var pluginNames = new HashSet<string>(plugins.Select(p => p.Name));
        var errors = new List<PluginValidationError>();

        foreach (var (pluginName, dependencies) in dependencyGraph.Dependencies)
        {
            foreach (var dependencyName in dependencies)
            {
                if (!pluginNames.Contains(dependencyName))
                {
                    errors.Add(new PluginValidationError(
                        PluginValidationErrorType.MissingDependency,
                        pluginName,
                        $"Plugin '{pluginName}' depends on '{dependencyName}' but this plugin was not found",
                        new Dictionary<string, object>
                        {
                            ["DependencyName"] = dependencyName,
                            ["AvailablePlugins"] = string.Join(", ", pluginNames.OrderBy(x => x))
                        }));
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates that plugin priority values are within reasonable ranges.
    /// </summary>
    /// <param name="plugins">The plugins to validate.</param>
    /// <returns>A list of validation errors for invalid priorities.</returns>
    private static List<PluginValidationError> ValidatePriorities(List<IPluginStartup> plugins)
    {
        var errors = new List<PluginValidationError>();

        foreach (var plugin in plugins)
        {
            var configureServicesPriority = plugin.ConfigureServicesPriority;
            var configurePriority = plugin.ConfigurePriority;

            // Check for extreme priority values that could cause issues
            const int MaxReasonablePriority = 10000;
            const int MinReasonablePriority = -10000;

            if (configureServicesPriority > MaxReasonablePriority || configureServicesPriority < MinReasonablePriority)
            {
                errors.Add(new PluginValidationError(
                    PluginValidationErrorType.InvalidPriority,
                    plugin.Name,
                    $"Plugin '{plugin.Name}' has ConfigureServicesPriority value {configureServicesPriority} which is outside reasonable range",
                    new Dictionary<string, object>
                    {
                        ["PriorityType"] = "ConfigureServicesPriority",
                        ["Value"] = configureServicesPriority,
                        ["RecommendedRange"] = $"[{MinReasonablePriority}, {MaxReasonablePriority}]"
                    }));
            }

            if (configurePriority > MaxReasonablePriority || configurePriority < MinReasonablePriority)
            {
                errors.Add(new PluginValidationError(
                    PluginValidationErrorType.InvalidPriority,
                    plugin.Name,
                    $"Plugin '{plugin.Name}' has ConfigurePriority value {configurePriority} which is outside reasonable range",
                    new Dictionary<string, object>
                    {
                        ["PriorityType"] = "ConfigurePriority",
                        ["Value"] = configurePriority,
                        ["RecommendedRange"] = $"[{MinReasonablePriority}, {MaxReasonablePriority}]"
                    }));
            }
        }

        return errors;
    }
}
