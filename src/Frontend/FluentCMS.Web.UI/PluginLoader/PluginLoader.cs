using System.Reflection;
using System.Collections.Concurrent;

namespace FluentCMS.Web.UI;

public class PluginLoader
{
    private readonly ConcurrentDictionary<string, Assembly> _loadedAssemblies = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Type>> _loadedTypes = new();

    private Assembly Load(string relativePath)
    {
        if (_loadedAssemblies.TryGetValue(relativePath, out Assembly? value))
            return value;

        var entryAssembly = Assembly.GetEntryAssembly() ??
            throw new InvalidOperationException("Entry assembly not found");

        var binFolder = Path.GetDirectoryName(entryAssembly.Location) ??
            throw new InvalidOperationException("Unable to determine bin folder path");

        string assemblyPath = Path.Combine(binFolder, relativePath);

        if (!Path.GetFullPath(assemblyPath).StartsWith(binFolder))
            throw new UnauthorizedAccessException("Attempted to load assembly from an illegal location");

        var customLoadContext = new PluginLoadContext();

        var assembly = customLoadContext.LoadFromAssemblyPath(assemblyPath);

        // Add to _loadedAssemblies using thread-safe method
        _loadedAssemblies.TryAdd(relativePath, assembly);

        return assembly;
    }

    public Type? GetType(string assemblyPath, string typeName)
    {
        var assembly = Load(assemblyPath);

        // Thread-safe check if the type already exists in the dictionary
        if (_loadedTypes.TryGetValue(assemblyPath, out var typeDict) && typeDict.TryGetValue(typeName, out Type? value))
            return value;

        var type = assembly.DefinedTypes.FirstOrDefault(x => x.Name == typeName) ??
            throw new TypeLoadException($"Type '{typeName}' not found in assembly '{assemblyPath}'");

        // Ensure that a type dictionary exists for this assembly
        var assemblyTypes = _loadedTypes.GetOrAdd(assemblyPath, _ => new ConcurrentDictionary<string, Type>());

        // Add the type to the dictionary in a thread-safe manner
        assemblyTypes.TryAdd(typeName, type);

        return type;
    }
}
