using System.Reflection;
using System.Text.RegularExpressions;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Plugins;

// Reads and checks the manifests, puts the plugins in order and runs their ConfigureServices. The order: the plugins are
// taken as they were added, explicit ones in the order of AddPlugin and then discovered ones in the order of the deps file,
// and each comes after the plugins it depends on, which move ahead of it when they would come later.
internal static partial class PluginHost
{
    private const int NameMaxLength = 32;

    public static SemanticVersion RunningSdk { get; } = SdkVersion();

    public static IReadOnlyList<RegisteredPlugin> Compose(IReadOnlyList<IPlugin> added, IReadOnlyList<Type> discovered, SemanticVersion sdk)
    {
        var candidates = added.Select(plugin => (Type: plugin.GetType(), Instance: (IPlugin?)plugin, Source: PluginSource.Explicit)).ToList();
        foreach (var type in discovered)
        {
            if (candidates.All(candidate => candidate.Type != type))
            {
                candidates.Add((type, null, PluginSource.Discovered));
            }
        }

        var manifests = candidates.Select(candidate => Read(candidate.Type, candidate.Instance, candidate.Source, sdk)).ToList();

        var duplicate = manifests.GroupBy(manifest => manifest.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            var types = duplicate.Select(manifest => manifest.Type).Distinct().ToList();
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.DuplicateName,
                types.Count == 1
                    ? $"The plugin '{duplicate.Key}' ({types[0].FullName}) is added more than once; add it with one AddPlugin call."
                    : $"The plugin name '{duplicate.Key}' is used by {string.Join(" and ", types.Select(type => type.FullName))}; each plugin needs a name of its own.");
        }

        var byName = manifests.ToDictionary(manifest => manifest.Name, StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            if (manifest.DependsOn.FirstOrDefault(dependency => !byName.ContainsKey(dependency)) is { } missing)
            {
                throw new FluentCmsConfigurationException(
                    PluginErrorCodes.MissingDependency,
                    $"The plugin '{manifest.Name}' ({manifest.Type.FullName}) depends on '{missing}', which is neither added with AddPlugin nor named by a referenced assembly.");
            }
        }

        var ordered = new List<Manifest>(manifests.Count);
        var visiting = new List<string>();
        var placed = new HashSet<string>(StringComparer.Ordinal);

        void Place(Manifest manifest)
        {
            if (placed.Contains(manifest.Name))
            {
                return;
            }

            var start = visiting.IndexOf(manifest.Name);
            if (start >= 0)
            {
                throw new FluentCmsConfigurationException(
                    PluginErrorCodes.DependencyCycle,
                    $"The plugins depend on each other in a cycle: {string.Join(" -> ", visiting.Skip(start).Append(manifest.Name).Select(name => $"'{name}'"))}.");
            }

            visiting.Add(manifest.Name);
            foreach (var dependency in manifest.DependsOn)
            {
                Place(byName[dependency]);
            }

            visiting.RemoveAt(visiting.Count - 1);
            placed.Add(manifest.Name);
            ordered.Add(manifest);
        }

        foreach (var manifest in manifests)
        {
            Place(manifest);
        }

        return [.. ordered.Select(manifest => new RegisteredPlugin(
            manifest.Name,
            manifest.Version,
            manifest.SdkRange,
            manifest.DependsOn,
            manifest.Type,
            manifest.Source,
            manifest.Instance ?? (IPlugin)Activator.CreateInstance(manifest.Type)!))];
    }

    // A hook or handler belongs to the plugin whose ConfigureServices registered its type, or else to the plugin whose class
    // is the only one in the hook's assembly.
    public static PluginRegistry ConfigureServices(FluentCmsBuilder cms, IReadOnlyList<RegisteredPlugin> plugins)
    {
        var types = new Dictionary<Type, int>();
        for (var index = 0; index < plugins.Count; index++)
        {
            var plugin = plugins[index];
            var before = new HashSet<ServiceDescriptor>(cms.Services, ReferenceEqualityComparer.Instance);
            plugin.Instance.ConfigureServices(new PluginServiceBuilder(cms, plugin.Name));

            types.TryAdd(plugin.Type, index);
            foreach (var descriptor in cms.Services.Where(descriptor => !before.Contains(descriptor)))
            {
                if (ImplementationOf(descriptor) is { } type)
                {
                    types.TryAdd(type, index);
                }
            }
        }

        var assemblies = plugins
            .Select((plugin, index) => (plugin.Type.Assembly, Index: index))
            .GroupBy(item => item.Assembly)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Index);

        return new PluginRegistry(plugins, types, assemblies);
    }

    private static Type? ImplementationOf(ServiceDescriptor descriptor) =>
        descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType ?? descriptor.KeyedImplementationInstance?.GetType()
            : descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();

    private static Manifest Read(Type type, IPlugin? instance, PluginSource source, SemanticVersion sdk)
    {
        if (instance is null && (!type.IsClass || type.IsAbstract || !typeof(IPlugin).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null))
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidPlugin,
                $"{type.FullName}, which [assembly: FluentCmsPlugin] in {type.Assembly.GetName().Name} names, is not a plugin: a plugin is a class that implements IPlugin and has a public parameterless constructor.");
        }

        var attributes = type.GetCustomAttributes<FluentCmsPluginAttribute>(inherit: false).ToList();
        if (attributes is not [{ Name: { } name } attribute])
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidPlugin,
                $"The plugin {type.FullName} needs one [FluentCmsPlugin(\"name\", SdkRange = \"...\")] with its name, and has {(attributes.Count == 0 ? "none" : attributes.Count == 1 ? "one that names a type" : attributes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))}.");
        }

        if (name.Length > NameMaxLength || !NamePattern().IsMatch(name))
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidName,
                $"The plugin {type.FullName} is named '{name}'; a plugin name is lower case letters and digits joined by single hyphens, starts with a letter and has at most {NameMaxLength} characters, such as 'media' or 'ai-assist'.");
        }

        var version = attribute.Version ?? StartupReportFactory.VersionOf(type.Assembly);
        if (attribute.Version is not null && SemanticVersion.Parse(attribute.Version) is null)
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidVersion,
                $"The plugin '{name}' ({type.FullName}) has Version '{attribute.Version}', which is not a semantic version such as 1.2.0.");
        }

        if (SdkRange.Parse(attribute.SdkRange) is not { } range)
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidSdkRange,
                attribute.SdkRange is null
                    ? $"The plugin '{name}' ({type.FullName}) has no SdkRange; name the FluentCMS.Sdk versions it runs on, such as SdkRange = \"[1.0.0,2.0.0)\"."
                    : $"The plugin '{name}' ({type.FullName}) has SdkRange '{attribute.SdkRange}', which is not NuGet interval notation such as [1.0.0,2.0.0).");
        }

        if (!range.Satisfies(sdk))
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.SdkNotSatisfied,
                $"The plugin '{name}' ({type.FullName}) runs on FluentCMS.Sdk {attribute.SdkRange}, and this host runs {sdk}.");
        }

        IReadOnlyList<string> dependsOn = [.. (attribute.DependsOn ?? []).Select(dependency => dependency ?? string.Empty).Distinct(StringComparer.Ordinal)];
        return new Manifest(name, version, attribute.SdkRange!, dependsOn, type, instance, source);
    }

    private static SemanticVersion SdkVersion()
    {
        var assembly = typeof(IPlugin).Assembly;
        return SemanticVersion.Parse(StartupReportFactory.VersionOf(assembly))
            ?? (assembly.GetName().Version is { } version ? SemanticVersion.Parse($"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}") : null)
            ?? SemanticVersion.Parse("0.0.0")!;
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    private sealed record Manifest(string Name, string Version, string SdkRange, IReadOnlyList<string> DependsOn, Type Type, IPlugin? Instance, PluginSource Source);
}
