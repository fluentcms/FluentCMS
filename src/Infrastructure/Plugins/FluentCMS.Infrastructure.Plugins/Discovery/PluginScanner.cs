using System.Reflection;

namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Implementation of IPluginScanner that uses reflection to discover plugin types.
/// Scans loaded assemblies for classes marked with [Plugin] attribute and instantiates them.
/// </summary>
public class PluginScanner : IPluginScanner
{
    /// <summary>
    /// Scans assemblies for plugin implementations based on the provided options.
    /// </summary>
    /// <param name="options">The plugin system options containing scanning configuration.</param>
    /// <param name="cancellationToken">Token to cancel the scanning operation.</param>
    /// <returns>A read-only list of discovered plugin startup instances.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="PluginDiscoveryException">Thrown when plugin discovery fails.</exception>
    public async Task<IReadOnlyList<IPluginStartup>> ScanForPlugins(PluginSystemOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var pluginTypes = await FindPluginTypes(options, cancellationToken);
            var plugins = await InstantiatePlugins(pluginTypes, cancellationToken);

            return plugins.AsReadOnly();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PluginDiscoveryException("Failed to scan for plugins", ex);
        }
    }

    /// <summary>
    /// Finds plugin types by scanning assemblies that match the configured patterns.
    /// </summary>
    /// <param name="options">The plugin system options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of plugin types found.</returns>
    private static async Task<List<Type>> FindPluginTypes(PluginSystemOptions options, CancellationToken cancellationToken)
    {
        var pluginTypes = new List<Type>();

        // Get all loaded assemblies
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (ShouldScanAssembly(assembly, options))
                {
                    var typesInAssembly = await Task.Run(() =>
                        FindPluginTypesInAssembly(assembly, cancellationToken), cancellationToken);

                    pluginTypes.AddRange(typesInAssembly);
                }
            }
            catch (Exception ex)
            {
                // Log the error but continue scanning other assemblies
                // Individual assembly failures shouldn't stop the entire scan
                Console.WriteLine($"Warning: Failed to scan assembly {assembly.FullName}: {ex.Message}");
            }
        }

        return pluginTypes;
    }

    /// <summary>
    /// Determines if an assembly should be scanned based on the configured patterns.
    /// </summary>
    /// <param name="assembly">The assembly to check.</param>
    /// <param name="options">The plugin system options.</param>
    /// <returns>True if the assembly should be scanned, false otherwise.</returns>
    private static bool ShouldScanAssembly(Assembly assembly, PluginSystemOptions options)
    {
        var assemblyName = assembly.GetName().Name;
        if (string.IsNullOrEmpty(assemblyName))
        {
            return false;
        }

        // Check if assembly name matches any of the configured patterns
        foreach (var pattern in options.ScanAssemblyPatterns)
        {
            if (assemblyName.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds plugin types within a specific assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of plugin types found in the assembly.</returns>
    private static List<Type> FindPluginTypesInAssembly(Assembly assembly, CancellationToken cancellationToken)
    {
        var pluginTypes = new List<Type>();

        try
        {
            var types = assembly.GetTypes();

            foreach (var type in types)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsPluginType(type))
                {
                    pluginTypes.Add(type);
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Handle types that couldn't be loaded
            foreach (var loaderException in ex.LoaderExceptions)
            {
                Console.WriteLine($"Warning: Failed to load type from {assembly.FullName}: {loaderException?.Message}");
            }

            // Still try to get the types that were loaded successfully
            if (ex.Types != null)
            {
                foreach (var type in ex.Types)
                {
                    if (type != null && IsPluginType(type))
                    {
                        pluginTypes.Add(type);
                    }
                }
            }
        }

        return pluginTypes;
    }

    /// <summary>
    /// Determines if a type is a valid plugin type.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is a valid plugin, false otherwise.</returns>
    private static bool IsPluginType(Type type)
    {
        // Check if the type has the PluginAttribute
        if (!type.IsDefined(typeof(PluginAttribute), false))
        {
            return false;
        }

        // Check if it implements IPluginStartup
        if (!typeof(IPluginStartup).IsAssignableFrom(type))
        {
            return false;
        }

        // Check if it's a class and not abstract
        if (!type.IsClass || type.IsAbstract)
        {
            return false;
        }

        // Check if it has a parameterless constructor
        if (type.GetConstructor(Type.EmptyTypes) == null)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Instantiates plugin instances from the discovered types.
    /// </summary>
    /// <param name="pluginTypes">The plugin types to instantiate.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of instantiated plugin startup instances.</returns>
    private static async Task<List<IPluginStartup>> InstantiatePlugins(List<Type> pluginTypes, CancellationToken cancellationToken)
    {
        var plugins = new List<IPluginStartup>();

        foreach (var type in pluginTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var plugin = await InstantiatePlugin(type, cancellationToken);
                plugins.Add(plugin);
            }
            catch (Exception ex)
            {
                // Log the error but continue with other plugins
                Console.WriteLine($"Warning: Failed to instantiate plugin {type.FullName}: {ex.Message}");
            }
        }

        return plugins;
    }

    /// <summary>
    /// Instantiates a single plugin instance using reflection.
    /// </summary>
    /// <param name="pluginType">The plugin type to instantiate.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The instantiated plugin startup instance.</returns>
    private static async Task<IPluginStartup> InstantiatePlugin(Type pluginType, CancellationToken cancellationToken)
    {
        // Use Task.Run to perform instantiation on background thread
        var plugin = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Create instance using parameterless constructor
            var instance = Activator.CreateInstance(pluginType);

            if (instance is not IPluginStartup pluginStartup)
            {
                throw new InvalidOperationException($"Type {pluginType.FullName} does not implement IPluginStartup");
            }

            return pluginStartup;
        }, cancellationToken);

        return plugin;
    }
}
