namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Implementation of IPluginScanner that uses reflection to discover plugin types.
/// Scans loaded assemblies for classes marked with [Plugin] attribute and instantiates them.
/// </summary>
internal abstract class PluginScanner(ILogger<PluginScanner> logger, IOptions<PluginSystemOptions> pluginSystemOptions)
{
    private readonly ILogger<PluginScanner> _logger = NullArgumentException.RequireNonNull(logger);
    private readonly PluginSystemOptions _pluginSystemOptions = NullArgumentException.RequireNonNull(pluginSystemOptions.Value);

    public IEnumerable<Type> GetPluginTypes(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executablePath = Assembly.GetExecutingAssembly().Location;
        var executableFolder = Path.GetDirectoryName(executablePath) ??
            throw new PluginDiscoveryException("Could not determine the executable folder path.");

        // Get all DLL files in the executable folder
        var allDllFiles = Directory.GetFiles(executableFolder, "*.dll", SearchOption.TopDirectoryOnly);


        foreach (var assemblyFilePath in allDllFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();  // Add this for per-iteration checks
            if (!IsNameMatched(assemblyFilePath))
                continue; // Skip non-matching assemblies

            var preloadedAssembly = FindLoaded(assemblyFilePath, cancellationToken);

            if (preloadedAssembly != null)
            {
                foreach (var type in FindPluginTypes(preloadedAssembly, cancellationToken))
                {
                    yield return type;
                }
            }
            else
            {
                // Not loaded: load into a collectible context
                var alc = new PluginLoadContext(assemblyFilePath);
                var newlyLoadedAssembly = alc.LoadFromAssemblyPath(assemblyFilePath);

                var pluginTypes = FindPluginTypes(newlyLoadedAssembly, cancellationToken).ToArray();
                if (pluginTypes.Length == 0)
                {
                    // No plugins -> unload immediately
                    alc.Unload();
                    GC.Collect();
                    GC.WaitForPendingFinalizers(); GC.Collect();
                    continue; // move to next assembly
                }

                foreach (var t in pluginTypes)
                    yield return t;

            }
        }
    }

    private bool IsNameMatched(string assemblyFileName)
    {
        var scanPatterns = _pluginSystemOptions.ScanAssemblyPatterns;

        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(assemblyFileName);
        if (!scanPatterns.All(pattern =>
            fileNameWithoutExt.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase)))
        {
            return false; // Skip non-matching assemblies
        }
        return true;
    }

    private static Assembly? FindLoaded(string assemblyPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Check if already loaded
        var asmName = AssemblyName.GetAssemblyName(assemblyPath);
        var loadedAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), asmName));

        return loadedAsm;
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
}
