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
        _pluginAssemblyPath = Path.GetDirectoryName(executablePath)
            ?? throw new PluginDiscoveryException("Could not determine the executable folder path.");

        _pluginAttributeFullName = typeof(PluginAttribute).FullName
            ?? throw new PluginDiscoveryException("Could not determine the full name of PluginAttribute.");

        _pluginStartupInterfaceFullName = typeof(IPluginStartup).FullName
            ?? throw new PluginDiscoveryException("Could not determine the full name of IPluginStartup.");

        var probeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // (1) Already loaded assemblies in the host (best-effort, skip dynamic/in-memory)
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var loc = SafeGetLocation(asm);
            if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                probeFiles.Add(loc);
        }

        // (2) Core runtime directory (System.Private.CoreLib, System.Runtime, etc.)
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        AddAllDlls(runtimeDir, probeFiles);

        // (3) Host base directory (bin/{Configuration}/{TFM})
        var baseDir = AppContext.BaseDirectory;
        AddAllDlls(baseDir, probeFiles);

        // (4) Plugin folder (where your plugins live)
        AddAllDlls(_pluginAssemblyPath, probeFiles);

        // (5) Shared frameworks: Microsoft.NETCore.App + Microsoft.AspNetCore.App
        // Try to discover via known types (works even in non-SDK hosts)
        //TryAddContainingDir(typeof(Microsoft.AspNetCore.Builder.WebApplication), probeFiles);  // AspNetCore.App
        TryAddContainingDir(typeof(ILogger), probeFiles);        // Extensions
        TryAddContainingDir(typeof(Enumerable), probeFiles);                                   // NETCore.App

        // Fallback: DOTNET_ROOT/shared paths (in case Locations are empty, e.g., single-file)
        TryAddDotnetShared(probeFiles);

        // IMPORTANT: PathAssemblyResolver wants file paths, not directories.
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

        try
        {
            using var mlc = new MetadataLoadContext(_resolver);
            var asm = mlc.LoadFromAssemblyPath(assemblyPath);

            foreach (var type in asm.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract) continue;

                var hasPluginAttribute = type.GetCustomAttributesData()
                    .Any(cad => string.Equals(cad.AttributeType.FullName, _pluginAttributeFullName, StringComparison.Ordinal));

                if (!hasPluginAttribute) continue;

                var implementsStartup = type.GetInterfaces()
                    .Any(i => string.Equals(i.FullName, _pluginStartupInterfaceFullName, StringComparison.Ordinal));

                if (implementsStartup) return true;
            }
            return false;
        }
        catch (FileNotFoundException fnf)
        {
            _logger.LogError(fnf, "MLC could not resolve dependency while scanning {Assembly}. Missing: {Missing}", assemblyPath, fnf.FileName);
            if (_pluginSystemOptions.IgnoreErrors) return false;
            throw new PluginDiscoveryException($"Failed to resolve '{fnf.FileName}' while scanning '{assemblyPath}'", fnf);
        }
    }

    static void AddAllDlls(string dir, HashSet<string> set)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
            set.Add(dll);
    }

    static void TryAddContainingDir(Type t, HashSet<string> set)
    {
        var loc = SafeGetLocation(t.Assembly);
        if (string.IsNullOrEmpty(loc)) return;
        var dir = Path.GetDirectoryName(loc);
        AddAllDlls(dir!, set);
    }

    static void TryAddDotnetShared(HashSet<string> set)
    {
        var dotnetRoot =
            Environment.GetEnvironmentVariable("DOTNET_ROOT") ??
            Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty) ?? string.Empty;

        var shared = Path.Combine(dotnetRoot, "shared");
        // Add Microsoft.NETCore.App and Microsoft.AspNetCore.App under /shared/*
        foreach (var product in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
        {
            var productDir = Path.Combine(shared, product);
            if (!Directory.Exists(productDir)) continue;

            foreach (var verDir in Directory.EnumerateDirectories(productDir))
                AddAllDlls(verDir, set);
        }
    }

    static string? SafeGetLocation(Assembly asm)
    {
        try { return asm.Location; }
        catch { return null; } // dynamic/single-file may throw or be empty
    }

}
