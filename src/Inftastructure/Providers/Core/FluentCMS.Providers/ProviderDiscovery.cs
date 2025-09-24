using FluentCMS.Providers.Abstractions;
using System.Collections.Concurrent;
using System.Reflection;

namespace FluentCMS.Providers;

internal class ProviderDiscovery(ProviderDiscoveryOptions options)
{
    public List<IProviderModule> GetProviderModules()
    {
        var executablePath = Assembly.GetExecutingAssembly().Location;

        var executanbleFolder = Path.GetDirectoryName(executablePath) ??
            throw new InvalidOperationException("Could not determine the executable folder path.");

        // Get all DLL files in the specified directory
        var allDllFiles = Directory.GetFiles(executanbleFolder, "*.dll", SearchOption.TopDirectoryOnly);

        var dllFiles = allDllFiles
            .Where(dllFilePath => options.AssemblyPrefixesToScan.Any(prefix => Path.GetFileName(dllFilePath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        // Use a thread-safe collection for parallel processing
        var providerModules = new ConcurrentBag<IProviderModule>();

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
                    // Extract valid types from the exception
                    allTypes = [.. ex.Types.Where(t => t != null)];
                }

                Array.ForEach(allTypes, type =>
                {
                    if (type != null && type.IsClass && !type.IsAbstract && typeof(IProviderModule).IsAssignableFrom(type))
                    {
                        providerModules.Add((IProviderModule)Activator.CreateInstance(type)!);
                    }
                });
            }
            catch (Exception)
            {
                if (!options.IgnoreExceptions)
                    throw;
            }
        });

        return [.. providerModules];
    }
}
