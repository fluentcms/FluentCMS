namespace FluentCMS.Plugins;

internal sealed class PluginManager : IPluginManager
{
    private readonly IEnumerable<IPluginMetadata> _pluginsMetaData;
    private readonly List<IPlugin> _pluginInstances = [];

    public PluginManager(string[] pluginPrefixes)
    {
        var executablePath = Assembly.GetExecutingAssembly().Location;

        var executanbleFolder = Path.GetDirectoryName(executablePath) ??
            throw new InvalidOperationException("Could not determine the executable folder path.");

        _pluginsMetaData = ScanAssemblies(executanbleFolder, pluginPrefixes);
    }

    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        foreach (var pluginMetaData in _pluginsMetaData)
        {
            try
            {
                //_logger.LogInformation("Configuring services for plugin: {PluginName} ({PluginVersion})", pluginMetaData.Name, pluginMetaData.Version);

                // Get the plugin type which implements IPlugin
                var pluginType = pluginMetaData.Type ??
                    throw new InvalidOperationException($"Plugin type not found for {pluginMetaData.Name}");

                // Create an instance of the plugin
                var plugin = Activator.CreateInstance(pluginType) as IPlugin ??
                    throw new InvalidOperationException($"Failed to create instance of plugin {pluginMetaData.Name}");

                _pluginInstances.Add(plugin);

                // Call the ConfigureServices method on the plugin
                plugin.ConfigureServices(builder);

                //_logger.LogInformation("Successfully configured services for plugin: {PluginName}", pluginMetaData.Name);
            }
            catch (Exception)
            {
                // Log the exception with detailed information
                //_logger.LogError(ex, "Failed to configure services for plugin {PluginName} ({PluginFileName}): {ErrorMessage}", pluginMetaData.Name, pluginMetaData.FileName, ex.Message);
            }
        }
    }

    public void Configure(IApplicationBuilder app)
    {
        foreach (var pluginInstance in _pluginInstances)
        {
            try
            {
                //_logger.LogInformation("Configuring plugin: {PluginType}", pluginInstance.GetType().FullName);

                // Call the Configure method on the plugin
                pluginInstance.Configure(app);

                //_logger.LogInformation("Successfully configured plugin: {PluginType}", pluginInstance.GetType().FullName);
            }
            catch (Exception)
            {
                //_logger.LogError(ex, "Failed to configure plugin {PluginType}: {ErrorMessage}", pluginInstance.GetType().FullName, ex.Message);
            }
        }
    }

    public IEnumerable<IPlugin> GetPlugins()
    {
        return _pluginInstances;
    }

    public IEnumerable<IPluginMetadata> GetPluginMetadata()
    {
        return _pluginsMetaData;
    }

    private static IEnumerable<IPluginMetadata> ScanAssemblies(string folderPath, string[] pluginPrefixes)
    {
        // Get all DLL files in the specified directory
        var allDllFiles = Directory.GetFiles(folderPath, "*.dll", SearchOption.TopDirectoryOnly);

        var dllFiles = allDllFiles
            .Where(dllFilePath => pluginPrefixes.Any(prefix => Path.GetFileName(dllFilePath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        //_logger.LogInformation("Found {DllCount} potential plugin DLL files", dllFiles.Length);

        // Use a thread-safe collection for parallel processing
        var pluginsMetaData = new ConcurrentBag<IPluginMetadata>();

        // Process assemblies in parallel
        Array.ForEach(dllFiles, dllPath =>
        {
            try
            {
                // Check if the assembly is already loaded before loading it
                var assemblyName = AssemblyName.GetAssemblyName(dllPath);
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), assemblyName)) ??
                    Assembly.LoadFrom(dllPath);

                // Get all types first to avoid multiple calls to GetTypes()
                Type?[] allTypes = [];
                try
                {
                    allTypes = assembly.GetExportedTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    //_logger.LogWarning(ex, "ReflectionTypeLoadException while loading types from {DllPath}. Using available types.", dllPath);

                    // Log the specific loader exceptions for better diagnostics
                    if (ex.LoaderExceptions != null)
                    {
                        foreach (var loaderEx in ex.LoaderExceptions)
                        {
                            if (loaderEx != null)
                            {
                                //_logger.LogWarning(loaderEx, "Loader exception details for {DllPath}", dllPath);
                            }
                        }
                    }

                    // Extract valid types from the exception
                    allTypes = [.. ex.Types.Where(t => t != null)];
                }

                foreach (var type in allTypes)
                {
                    if (type != null && type.IsClass && !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type))
                    {
                        var descriptionAttribute = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>();

                        //_logger.LogInformation("Found plugin: {PluginName} in assembly {AssemblyName}", type.FullName, assemblyName?.Name);

                        var pluginMetaData = new PluginMetadata
                        {
                            Name = assemblyName?.Name ?? type.Name,
                            Description = descriptionAttribute?.Description ?? "Not specified",
                            Version = assemblyName?.Version?.ToString() ?? string.Empty,
                            FileName = Path.GetFileName(dllPath),
                            Assembly = assembly,
                            Type = type
                        };

                        pluginsMetaData.Add(pluginMetaData);
                    }
                }
            }
            catch (Exception ex) when (
                ex is BadImageFormatException ||
                ex is FileLoadException ||
                ex is FileNotFoundException)
            {
                //_logger.LogWarning(ex, "Failed to load assembly: {DllPath}", dllPath);
            }
            catch (Exception)
            {
                //_logger.LogError(ex, "Unexpected error loading assembly: {DllPath}", dllPath);
            }
        });

        //_logger.LogInformation("Successfully identified {PluginCount} plugins", pluginsMetaData.Count);
        return pluginsMetaData.AsEnumerable();
    }
}
