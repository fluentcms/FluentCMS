namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Defines the contract for scanning and discovering plugin types.
/// </summary>
public interface IPluginScanner
{
    /// <summary>
    /// Retrieves a list of plugin types from assemblies in the executable folder.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of discovered plugin types.</returns>
    List<Type> GetPluginTypes(CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of IPluginScanner that uses reflection to discover plugin types.
/// Scans loaded assemblies for classes marked with [Plugin] attribute and instantiates them.
/// </summary>
/// <param name="logger">The logger instance for logging operations.</param>
/// <param name="pluginSystemOptions">The options for the plugin system configuration.</param>
internal abstract class PluginScanner(ILogger<PluginScanner> logger, IOptions<PluginSystemOptions> pluginSystemOptions) : IPluginScanner
{
    private readonly ILogger<PluginScanner> _logger = NullArgumentException.RequireNonNull(logger);
    private readonly PluginSystemOptions _pluginSystemOptions = NullArgumentException.RequireNonNull(pluginSystemOptions.Value);

    public List<Type> GetPluginTypes(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var types = new List<Type>();

        try
        {
            var executablePath = Assembly.GetExecutingAssembly().Location;
            var executableFolder = Path.GetDirectoryName(executablePath) ??
                throw new PluginDiscoveryException("Could not determine the executable folder path.");

            _logger.LogInformation("Starting plugin type discovery in folder {Folder}", executableFolder);

            // Get all DLL files in the executable folder
            var allDllFiles = Directory.GetFiles(executableFolder, "*.dll", SearchOption.TopDirectoryOnly);

            foreach (var assemblyFilePath in allDllFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();  // Add this for per-iteration checks
                try
                {
                    if (!IsNameMatched(assemblyFilePath))
                    {
                        _logger.LogDebug("Skipping assembly {Assembly} as it does not match scan patterns", assemblyFilePath);
                        continue; // Skip non-matching assemblies
                    }

                    var preloadedAssembly = FindLoaded(assemblyFilePath, cancellationToken);

                    if (preloadedAssembly != null)
                    {
                        _logger.LogDebug("Using preloaded assembly {Assembly}", assemblyFilePath);
                        types.AddRange(FindPluginTypes(preloadedAssembly, cancellationToken));
                    }
                    else
                    {
                        // Not loaded: load into a collectible context
                        _logger.LogDebug("Loading assembly {Assembly} into collectible context", assemblyFilePath);
                        var alc = new PluginLoadContext(assemblyFilePath);
                        var newlyLoadedAssembly = alc.LoadFromAssemblyPath(assemblyFilePath);

                        var pluginTypes = FindPluginTypes(newlyLoadedAssembly, cancellationToken).ToArray();
                        if (pluginTypes.Length > 0)
                        {
                            types.AddRange(pluginTypes);
                        }
                        else
                        {
                            // No plugins -> unload immediately
                            _logger.LogDebug("No plugin types found in {Assembly}, unloading", assemblyFilePath);
                            alc.Unload();
                            GC.Collect();
                            GC.WaitForPendingFinalizers(); GC.Collect();
                            continue; // move to next assembly
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error processing assembly {Assembly}", assemblyFilePath);
                    if (_pluginSystemOptions.IgnoreErrors)
                    {
                        _logger.LogWarning("Ignoring error in assembly {Assembly} due to configuration", assemblyFilePath);
                        continue; // Skip this assembly and continue
                    }
                    throw new PluginDiscoveryException($"Failed to process assembly {assemblyFilePath}", ex);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during plugin type discovery");
            throw new PluginDiscoveryException("Plugin discovery failed", ex);
        }
        _logger.LogInformation("Discovered {Count} plugin types", types.Count);
        return types;
    }

    /// <summary>
    /// Checks if the assembly file name matches the configured scan patterns.
    /// </summary>
    /// <param name="assemblyFileName">The full path to the assembly file.</param>
    /// <returns>True if the file name matches all patterns; otherwise, false.</returns>
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

    /// <summary>
    /// Attempts to find an already loaded assembly matching the given path.
    /// </summary>
    /// <param name="assemblyPath">The full path to the assembly file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The loaded assembly if found; otherwise, null.</returns>
    private Assembly? FindLoaded(string assemblyPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Check if already loaded
            var asmName = AssemblyName.GetAssemblyName(assemblyPath);
            var loadedAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), asmName));

            return loadedAsm;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error finding loaded assembly {Assembly}", assemblyPath);
            if (_pluginSystemOptions.IgnoreErrors)
            {
                _logger.LogWarning(ex, "Ignoring error finding loaded assembly {Assembly} due to configuration", assemblyPath);
                return null;
            }
            throw new PluginDiscoveryException($"Failed to find loaded assembly {assemblyPath}", ex);
        }
    }

    /// <summary>
    /// Finds plugin types in the given assembly that are marked with the PluginAttribute and implement IPluginStartup.
    /// </summary>
    /// <param name="assembly">The assembly to scan for plugin types.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An enumerable of plugin types found in the assembly.</returns>
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
