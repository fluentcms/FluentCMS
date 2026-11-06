using System.Reflection;
using System.Text.Json;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Plugins;

namespace FluentCMS.Core.Plugins;

// The plugin classes named by [assembly: FluentCmsPlugin(typeof(T))] in the libraries of the app's deps.json that depend
// on the SDK, in the order of that file. Read once per process: the file does not change while the app runs. An app with
// no deps file on disk, such as a single-file one, discovers nothing and adds its plugins explicitly.
internal static class PluginDiscovery
{
    private const string Sdk = "FluentCMS.Sdk";

    private static readonly Lazy<IReadOnlyList<Type>> discovered = new(Discover);

    public static IReadOnlyList<Type> Types => discovered.Value;

    private static List<Type> Discover()
    {
        var types = new List<Type>();
        foreach (var assembly in Assemblies())
        {
            foreach (var attribute in assembly.GetCustomAttributes<FluentCmsPluginAttribute>())
            {
                if (attribute.PluginType is null)
                {
                    throw new FluentCmsConfigurationException(
                        PluginErrorCodes.InvalidPlugin,
                        $"The assembly {assembly.GetName().Name} has [assembly: FluentCmsPlugin(\"{attribute.Name}\")]; on an assembly the attribute names a plugin class, as in [assembly: FluentCmsPlugin(typeof(MyPlugin))].");
                }

                if (!types.Contains(attribute.PluginType))
                {
                    types.Add(attribute.PluginType);
                }
            }
        }

        return types;
    }

    private static IEnumerable<Assembly> Assemblies()
    {
        if (DepsFile() is not { } path)
        {
            yield break;
        }

        foreach (var name in AssembliesOfSdkDependents(path))
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.Load(new AssemblyName(name));
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                continue;
            }

            yield return assembly;
        }
    }

    // The host lists the app's own deps file first, then those of the shared frameworks.
    private static string? DepsFile()
    {
        var listed = (AppContext.GetData("APP_CONTEXT_DEPS_FILES") as string)?
            .Split([';', Path.PathSeparator], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(file => file.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase));
        if (listed is not null && File.Exists(listed))
        {
            return listed;
        }

        var entry = Assembly.GetEntryAssembly()?.GetName().Name;
        var beside = entry is null ? null : Path.Combine(AppContext.BaseDirectory, $"{entry}.deps.json");
        return beside is not null && File.Exists(beside) ? beside : null;
    }

    private static List<string> AssembliesOfSdkDependents(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var target = root.TryGetProperty("runtimeTarget", out var runtimeTarget)
            && runtimeTarget.TryGetProperty("name", out var targetName)
            && targets.TryGetProperty(targetName.GetString() ?? string.Empty, out var named)
                ? named
                : targets.EnumerateObject().Select(property => property.Value).FirstOrDefault();
        if (target.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var libraries = new List<(string Name, List<string> Dependencies, List<string> Assemblies)>();
        foreach (var library in target.EnumerateObject())
        {
            var name = library.Name.Split('/')[0];
            var dependencies = library.Value.TryGetProperty("dependencies", out var listed) && listed.ValueKind == JsonValueKind.Object
                ? listed.EnumerateObject().Select(dependency => dependency.Name).ToList()
                : [];
            var assemblies = library.Value.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object
                ? runtime.EnumerateObject()
                    .Select(asset => asset.Name)
                    .Where(asset => asset.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .Select(asset => Path.GetFileNameWithoutExtension(asset.Replace('\\', '/').Split('/')[^1]))
                    .ToList()
                : [];
            libraries.Add((name, dependencies, assemblies));
        }

        var byName = libraries.GroupBy(library => library.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Dependencies, StringComparer.OrdinalIgnoreCase);
        var reaches = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        bool ReachesSdk(string name)
        {
            if (reaches.TryGetValue(name, out var known))
            {
                return known;
            }

            reaches[name] = false;
            var result = byName.TryGetValue(name, out var dependencies)
                && dependencies.Any(dependency => string.Equals(dependency, Sdk, StringComparison.OrdinalIgnoreCase) || ReachesSdk(dependency));
            reaches[name] = result;
            return result;
        }

        return [.. libraries
            .Where(library => !string.Equals(library.Name, Sdk, StringComparison.OrdinalIgnoreCase) && ReachesSdk(library.Name))
            .SelectMany(library => library.Assemblies)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
