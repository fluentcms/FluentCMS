using System.Collections.Frozen;
using System.Reflection;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Plugins;

internal enum PluginSource
{
    Explicit,
    Discovered,
}

internal sealed record RegisteredPlugin(
    string Name,
    string Version,
    string SdkRange,
    IReadOnlyList<string> DependsOn,
    Type Type,
    PluginSource Source,
    IPlugin Instance);

// The plugins of the host, in plugin order. As the hook tiebreaker it ranks a hook or handler by the position of its plugin,
// all below 0, so that one of no plugin, the host's or core's, ranks after every plugin's and keeps its registration order.
internal sealed class PluginRegistry : IHookTiebreaker
{
    private readonly FrozenDictionary<Type, int> _types;
    private readonly FrozenDictionary<Assembly, int> _assemblies;
    private int _configured;

    public PluginRegistry(IReadOnlyList<RegisteredPlugin> plugins, IReadOnlyDictionary<Type, int> types, IReadOnlyDictionary<Assembly, int> assemblies)
    {
        Plugins = plugins;
        _types = types.ToFrozenDictionary(item => item.Key, item => item.Value - plugins.Count);
        _assemblies = assemblies.ToFrozenDictionary(item => item.Key, item => item.Value - plugins.Count);
    }

    public IReadOnlyList<RegisteredPlugin> Plugins { get; }

    // Configure runs once per host, however many times MapFluentCms is called.
    public bool TryStartConfigure() => Interlocked.Exchange(ref _configured, 1) == 0;

    public int Rank(Type hookType) =>
        _types.TryGetValue(hookType, out var rank)
        || (hookType.IsConstructedGenericType && _types.TryGetValue(hookType.GetGenericTypeDefinition(), out rank))
        || _assemblies.TryGetValue(hookType.Assembly, out rank)
            ? rank
            : 0;
}

internal sealed class PluginServiceBuilder(FluentCmsBuilder cms, string name) : IPluginServiceBuilder
{
    public string Name => name;

    public IServiceCollection Services => cms.Services;
}

internal sealed class PluginAppBuilder(string name, IServiceProvider services) : IPluginAppBuilder
{
    public string Name => name;

    public IServiceProvider Services => services;
}
