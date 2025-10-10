using Microsoft.Extensions.Options;
using System.Reflection;

namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Implementation of IPluginScanner that uses reflection to discover plugin types.
/// Scans loaded assemblies for classes marked with [Plugin] attribute and instantiates them.
/// </summary>
internal abstract class PluginScanner(ILogger<PluginScanner> logger, IOptions<PluginSystemOptions> pluginSystemOptions) : IPluginScanner
{
    private readonly ILogger<PluginScanner> _logger = NullArgumentException.RequireNonNull(logger);
    private readonly PluginSystemOptions _pluginSystemOptions = NullArgumentException.RequireNonNull(pluginSystemOptions.Value);
    // Attribute identification (stable across load contexts)
    private readonly string _attrFullName = typeof(PluginAttribute).FullName!;                    // e.g. "FluentCMS.Abstractions.PluginAttribute"
    private readonly string _attrAsmSimple = typeof(PluginAttribute).Assembly.GetName().Name!;    // e.g. "FluentCMS.Abstractions"

    public IEnumerable<Type> GetPluginTypes(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var matchedFiles = new List<string>();

        try
        {
            var executablePath = Assembly.GetExecutingAssembly().Location;
            var executableFolder = Path.GetDirectoryName(executablePath) ??
                throw new PluginDiscoveryException("Could not determine the executable folder path.");

            // Get all DLL files in the executable folder
            var allDllFiles = Directory.GetFiles(executableFolder, "*.dll", SearchOption.TopDirectoryOnly);
            var scanPatterns = _pluginSystemOptions.ScanAssemblyPatterns;

            foreach (var file in allDllFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();  // Add this for per-iteration checks
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
                if (scanPatterns.Any(pattern =>
                    fileNameWithoutExt.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase)))
                {
                    // Pattern matches: include this assembly
                    _logger.LogDebug("Scanning assembly {AssemblyPath} for plugins", file);
                    matchedFiles.Add(file);
                }
            }

            return LoadFast(matchedFiles, cancellationToken);
        }
        catch (Exception)
        {

            throw;
        }

        
    }

    private IEnumerable<Type> LoadFast(IEnumerable<string> assemblyPaths, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Create a set of file names from assembly paths for efficient lookup
        var pathFileNames = new HashSet<string>(assemblyPaths.Select(p => Path.GetFileNameWithoutExtension(p)), StringComparer.OrdinalIgnoreCase);

        // Get all loaded assemblies
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

        // First, yield already loaded assemblies that match the paths
        foreach (var asm in loadedAssemblies)
        {
            var name = asm.GetName().Name;
            if (!string.IsNullOrEmpty(name) && pathFileNames.Contains(name))
            {
                // Find the types refercing PluginAttribute or referencing its assembly

                foreach (var type in FindPluginTypes(asm, cancellationToken))
                {
                    yield return type;
                }
                pathFileNames.Remove(name); // Remove to avoid loading again
            }
        }

        // Then, load and yield assemblies for remaining paths
        foreach (var assemblyPath in assemblyPaths)
        {
            if (MetadataOnlyHasAttribute(assemblyPath, cancellationToken))
            {
                // Attribute found: load fully into a collectible PluginLoadContext
                var alc = new PluginLoadContext(assemblyPath);
                var asm = alc.LoadFromAssemblyPath(assemblyPath);
                foreach (var type in FindPluginTypes(asm, cancellationToken))
                {
                    yield return type;
                }
            }
        }
    }

    private bool MetadataOnlyHasAttribute(string assemblyPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Build a resolver with the runtime core + the target assembly directory so references can be resolved
        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var coreDlls = Directory.GetFiles(coreDir, "*.dll");

        // include the candidate assembly + its directory contents to help resolve its deps
        var pluginDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
        var pluginDlls = Directory.GetFiles(pluginDir, "*.dll");

        var resolver = new PathAssemblyResolver(coreDlls
            .Concat(pluginDlls)
            .Append(assemblyPath));

        using var mlc = new MetadataLoadContext(resolver);
        var asm = mlc.LoadFromAssemblyPath(assemblyPath);

        foreach (var t in asm.DefinedTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Similar to runtime path: compare by name/assembly
            foreach (var cad in t.GetCustomAttributesData())
            {
                var at = cad.AttributeType;
                var aAsm = at.Assembly.GetName().Name;
                if (at.FullName == _attrFullName && aAsm == _attrAsmSimple)
                    return true;
            }
        }
        return false;
    }

    private static IEnumerable<Type> FindPluginTypes(Assembly assembly, CancellationToken cancellationToken = default)
    {
        if (assembly.GetReferencedAssemblies().Any(a => a.Name == typeof(PluginAttribute).Assembly.GetName().Name))
        {
            foreach (var type in assembly.GetTypes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (type.IsDefined(typeof(PluginAttribute), inherit: false) &&
                    typeof(IPluginStartup).IsAssignableFrom(type) &&
                    type.IsClass && !type.IsAbstract &&
                    type.GetConstructor(Type.EmptyTypes) != null)
                {
                    yield return type;
                }
            }
        }
    }


    /// <summary>
    /// Finds plugin types by scanning assemblies that match the configured patterns.
    /// </summary>
    /// <param name="options">The plugin system options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of plugin types found.</returns>
    private static async Task<List<Type>> FindPluginTypes(CancellationToken cancellationToken)
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
