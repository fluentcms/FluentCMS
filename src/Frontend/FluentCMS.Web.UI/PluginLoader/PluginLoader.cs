using System.Reflection;

namespace FluentCMS.Web.UI;

public class PluginLoader
{
    private readonly Dictionary<string, Assembly> _loadedAssemblies = [];
    private readonly Dictionary<string, Dictionary<string, Type>> _loadedTypes = [];

    private Assembly Load(string relativePath)
    {
        if (_loadedAssemblies.ContainsKey(relativePath))
            return _loadedAssemblies[relativePath];

        var entryAssembly = Assembly.GetEntryAssembly() ??
            throw new InvalidOperationException("Entry assembly not found");

        var binFolder = Path.GetDirectoryName(entryAssembly.Location);

        string assemblyPath = Path.Combine(binFolder!, relativePath);

        if (!Path.GetFullPath(assemblyPath).StartsWith(binFolder!))
            throw new Exception("Attempted to load assembly from illegal location");

        var customLoadContext = new PluginLoadContext();

        var assembly = customLoadContext.LoadFromAssemblyPath(assemblyPath);

        _loadedAssemblies.Add(relativePath, assembly);

        return assembly;
    }

    public Type? GetType(string assemblyPath, string typeName)
    {
        var assembly = Load(assemblyPath);

        if (_loadedTypes.ContainsKey(assemblyPath) && _loadedTypes[assemblyPath].ContainsKey(typeName))
            return _loadedTypes[assemblyPath][typeName];

        var type = assembly.DefinedTypes.FirstOrDefault(x => x.Name == typeName);

        if (!_loadedTypes.ContainsKey(assemblyPath))
            _loadedTypes.Add(assemblyPath, []);

        if (!_loadedTypes[assemblyPath].ContainsKey(typeName))
            _loadedTypes[assemblyPath].Add(typeName, type);

        return type;
    }
}
