namespace FluentCMS.Infrastructure.Plugins.Discovery;

internal interface IPluginDiscovery
{
    List<string> Scan(CancellationToken cancellationToken = default);
}

internal class PluginDiscovery(ILogger<PluginDiscovery> logger, PluginSystemOptions pluginSystemOptions) : IPluginDiscovery
{
    private readonly ILogger<PluginDiscovery> _logger = NullArgumentException.RequireNonNull(logger);
    private readonly PluginSystemOptions _pluginSystemOptions = NullArgumentException.RequireNonNull(pluginSystemOptions);
    private string _pluginAssemblyPath = default!;
    private string _pluginAttributeFullName = default!;
    private string _pluginStartupInterfaceFullName = default!;
    private PathAssemblyResolver _resolver = default!;

    public List<string> Scan(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new List<string>();
        try
        {
            _logger.LogInformation("Initializing plugin discovery...");
            Init();
            _logger.LogInformation("Plugin discovery initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize plugin discovery.");
            if (_pluginSystemOptions.IgnoreErrors)
            {
                _logger.LogWarning("Ignoring initialization error due to configuration.");
                return result; // Return empty list on init failure if ignoring errors
            }
            throw new PluginDiscoveryException("Failed to initialize plugin discovery.", ex);
        }

        _logger.LogInformation("Starting plugin type discovery in folder {Folder}", _pluginAssemblyPath);

        // Get all DLL files in the executable folder
        var allDllFiles = Directory.GetFiles(_pluginAssemblyPath, "*.dll", SearchOption.TopDirectoryOnly);
        var assemblyFiles = new List<string>();
        try
        {
            foreach (var assemblyFilePath in allDllFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();  // Add this for per-iteration checks

                if (!IsNameMatched(assemblyFilePath))
                {
                    _logger.LogDebug("Skipping assembly {Assembly} as it does not match scan patterns", assemblyFilePath);
                    continue; // Skip non-matching assemblies
                }
                _logger.LogInformation("Found assembly matching scan patterns: {Assembly}", assemblyFilePath);
                assemblyFiles.Add(assemblyFilePath);
            }
            _logger.LogInformation("Found {Count} assemblies matching scan patterns", assemblyFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during assembly scanning in folder {Folder}", _pluginAssemblyPath);
            if (_pluginSystemOptions.IgnoreErrors)
            {
                _logger.LogWarning("Ignoring scanning error due to configuration.");
                return result; // Return empty list on scan failure if ignoring errors
            }
            else
            {
                throw new PluginDiscoveryException("Error during assembly scanning.", ex);
            }
        }

        foreach (var assemblyPath in assemblyFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();  // Add this for per-iteration checks
            try
            {
                if (AssemblyHasPlugin(assemblyPath))
                {
                    result.Add(assemblyPath);
                    _logger.LogInformation("Discovered plugin assembly: {Assembly}", assemblyPath);
                }
                else
                {
                    _logger.LogDebug("Assembly {Assembly} does not contain any plugins", assemblyPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error plugin discovery processing for assembly {Assembly}", assemblyPath);
                if (_pluginSystemOptions.IgnoreErrors)
                {
                    _logger.LogWarning(ex, "Error plugin discovery processing for assembly {Assembly}", assemblyPath);
                }
                else
                {
                    throw new PluginDiscoveryException($"Error processing assembly {assemblyPath}", ex);
                }
            }
        }
        _logger.LogInformation("Plugin discovery completed. Found {Count} plugin assemblies.", result.Count);
        return result;
    }

    private void Init()
    {

        var executablePath = Assembly.GetExecutingAssembly().Location;
        _pluginAssemblyPath = Path.GetDirectoryName(executablePath) ??
            throw new PluginDiscoveryException("Could not determine the executable folder path.");

        _pluginAttributeFullName = typeof(PluginAttribute).FullName ??
            throw new PluginDiscoveryException("Could not determine the full name of PluginAttribute.");

        _pluginStartupInterfaceFullName = typeof(IPluginStartup).FullName ??
            throw new PluginDiscoveryException("Could not determine the full name of IPluginStartup.");

        // Build a resolver set:
        // - Core runtime assemblies (System.Private.CoreLib, System.Runtime, etc.)
        // - All DLLs in the target assembly's folder (typical plugin deps live here)
        // - Any extra directories the caller provided (for shared abstractions)
        var probeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Core runtime directory
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in Directory.EnumerateFiles(runtimeDir, "*.dll"))
            probeFiles.Add(dll);

        // Target assembly directory
        foreach (var dll in Directory.EnumerateFiles(_pluginAssemblyPath, "*.dll"))
            probeFiles.Add(dll);

        // Ensure the target assembly itself is resolvable
        probeFiles.Add(_pluginAssemblyPath);

        _resolver = new PathAssemblyResolver(probeFiles);
    }

    /// <summary>
    /// Checks if the assembly file name matches the configured scan patterns.
    /// </summary>
    /// <param name="assemblyFileName">The full path to the assembly file.</param>
    /// <returns>True if the file name matches all patterns; otherwise, false.</returns>
    private bool IsNameMatched(string assemblyFileName)
    {
        var scanPatterns = _pluginSystemOptions.ScanAssemblyPatterns;

        if (!scanPatterns.Any(pattern =>
            assemblyFileName.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase)))
        {
            return false; // Skip non-matching assemblies
        }
        return true;
    }

    private bool AssemblyHasPlugin(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new PluginDiscoveryException("Assembly path cannot be null or empty.");

        assemblyPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(assemblyPath))
            throw new PluginDiscoveryException($"Assembly file not found: {assemblyPath}");

        using var mlc = new MetadataLoadContext(_resolver);

        // Load the target assembly inside this MLC
        var asm = mlc.LoadFromAssemblyPath(assemblyPath);

        foreach (var type in asm.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract) continue;

            // 1) Has [PluginAttribute] (match by full name)
            bool hasPluginAttribute = type
                .GetCustomAttributesData()
                .Any(cad => string.Equals(cad.AttributeType.FullName, _pluginAttributeFullName, StringComparison.Ordinal));

            if (!hasPluginAttribute) continue;

            // 2) Implements IPlugin (match by full name)
            bool implementsIPlugin = type
                .GetInterfaces()
                .Any(i => string.Equals(i.FullName, _pluginStartupInterfaceFullName, StringComparison.Ordinal));

            if (implementsIPlugin)
                return true;
        }

        return false;
    }

}
