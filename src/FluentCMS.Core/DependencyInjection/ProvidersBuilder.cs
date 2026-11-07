using FluentCMS.Diagnostics;
using FluentCMS.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.DependencyInjection;

// What fills a slot: the host's last Use of it, else a plugin's, else a service registered for the slot directly, else the
// default. A replacement takes out every other registration of the slot when AddFluentCms ends, so it wins whatever the order
// of the calls; a registration made after AddFluentCms still comes last and wins, as it would for any service.
public sealed class ProvidersBuilder : IProviderSlots
{
    private readonly List<ServiceDescriptor> _defaults = [];
    private readonly Dictionary<Type, Type> _host = [];
    private readonly Dictionary<Type, List<(string Plugin, Type Implementation)>> _plugins = [];
    private HashSet<ServiceDescriptor>? _beforeCore;

    internal ProvidersBuilder()
    {
    }

    public ProvidersBuilder Use<TSlot, TImplementation>()
        where TSlot : class
        where TImplementation : class, TSlot
    {
        _host[typeof(TSlot)] = typeof(TImplementation);
        return this;
    }

    IProviderSlots IProviderSlots.Use<TSlot, TImplementation>() => Use<TSlot, TImplementation>();

    // For the plugin builder: the replacements of one plugin, which the host's win over.
    internal IProviderSlots For(string plugin) => new PluginSlots(this, plugin);

    internal ProvidersBuilder Default<TSlot, TImplementation>()
        where TSlot : class
        where TImplementation : class, TSlot =>
        Default(ServiceDescriptor.Singleton<TSlot, TImplementation>());

    internal ProvidersBuilder Default<TSlot>(Func<IServiceProvider, TSlot> factory)
        where TSlot : class =>
        Default(ServiceDescriptor.Singleton(factory));

    private ProvidersBuilder Default(ServiceDescriptor descriptor)
    {
        if (_defaults.Any(slot => slot.ServiceType == descriptor.ServiceType))
        {
            throw new InvalidOperationException($"The provider slot {descriptor.ServiceType.FullName} has a default already.");
        }

        _defaults.Add(descriptor);
        return this;
    }

    // Called before core adds its services: a registration of a slot from before is the host's or a plugin's.
    internal void BeforeCore(IServiceCollection services) =>
        _beforeCore = new HashSet<ServiceDescriptor>(services, ReferenceEqualityComparer.Instance);

    internal ProviderSlotRegistry Apply(IServiceCollection services)
    {
        if (_host.Keys.FirstOrDefault(slot => !IsSlot(slot)) is { } unknown)
        {
            throw NotASlot(unknown, "The host replaces");
        }

        if (_plugins.FirstOrDefault(item => !IsSlot(item.Key)) is { Key: { } notSlot, Value: [var first, ..] })
        {
            throw NotASlot(notSlot, $"The plugin '{first.Plugin}' replaces");
        }

        var choices = new List<ProviderSlotChoice>(_defaults.Count);
        foreach (var slot in _defaults)
        {
            var type = slot.ServiceType;
            if (_host.TryGetValue(type, out var chosen))
            {
                choices.Add(new ProviderSlotChoice(type, Replace(services, type, chosen), ProviderSlotSources.Host, null));
            }
            else if (_plugins.TryGetValue(type, out var replacements))
            {
                var plugins = replacements.Select(item => item.Plugin).Distinct(StringComparer.Ordinal).ToList();
                if (plugins.Count > 1)
                {
                    var each = plugins.Select(plugin => $"'{plugin}' ({replacements.Last(item => item.Plugin == plugin).Implementation.FullName})");
                    throw new FluentCmsConfigurationException(
                        ProviderErrorCodes.SeveralPluginsReplaceSlot,
                        $"The plugins {Join(each)} all replace the provider slot {type.FullName}, which has one implementation. Choose it in the host with cms.Providers.Use<{type.Name}, T>().");
                }

                choices.Add(new ProviderSlotChoice(type, Replace(services, type, replacements[^1].Implementation), ProviderSlotSources.Plugin, plugins[0]));
            }
            else if (services.Any(descriptor => Fills(descriptor, type) && RegisteredBeforeCore(descriptor)))
            {
                choices.Add(new ProviderSlotChoice(type, null, ProviderSlotSources.Services, null));
            }
            else
            {
                // What the framework registered for the slot on core's behalf, such as the memory cache of Razor Pages,
                // gives way to the default.
                choices.Add(new ProviderSlotChoice(type, Replace(services, type, slot), ProviderSlotSources.Default, null));
            }
        }

        return new ProviderSlotRegistry(services, choices);
    }

    private void UseFor(string plugin, Type slot, Type implementation)
    {
        if (!_plugins.TryGetValue(slot, out var replacements))
        {
            _plugins[slot] = replacements = [];
        }

        replacements.Add((plugin, implementation));
    }

    private bool RegisteredBeforeCore(ServiceDescriptor descriptor) => _beforeCore?.Contains(descriptor) ?? true;

    private bool IsSlot(Type type) => _defaults.Any(slot => slot.ServiceType == type);

    private FluentCmsConfigurationException NotASlot(Type type, string who) => new(
        ProviderErrorCodes.NotASlot,
        $"{who} {type.FullName} through Providers.Use, and it is not a provider slot. The slots are {Join(_defaults.Select(slot => slot.ServiceType.FullName!))}.");

    private static ServiceDescriptor Replace(IServiceCollection services, Type slot, Type implementation) =>
        Replace(services, slot, ServiceDescriptor.Singleton(slot, implementation));

    private static ServiceDescriptor Replace(IServiceCollection services, Type slot, ServiceDescriptor descriptor)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (Fills(services[index], slot))
            {
                services.RemoveAt(index);
            }
        }

        services.Add(descriptor);
        return descriptor;
    }

    internal static bool Fills(ServiceDescriptor descriptor, Type slot) => !descriptor.IsKeyedService && descriptor.ServiceType == slot;

    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 1 ? list[0] : $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}";
    }

    private sealed class PluginSlots(ProvidersBuilder providers, string plugin) : IProviderSlots
    {
        public IProviderSlots Use<TSlot, TImplementation>()
            where TSlot : class
            where TImplementation : class, TSlot
        {
            providers.UseFor(plugin, typeof(TSlot), typeof(TImplementation));
            return this;
        }
    }
}

internal sealed record ProviderSlotChoice(Type Slot, ServiceDescriptor? Descriptor, string Source, string? Plugin);

internal static class ProviderSlotSources
{
    public const string Default = "default";
    public const string Host = "host";
    public const string Plugin = "plugin";
    public const string Services = "services";
}

internal static class ProviderErrorCodes
{
    public const string SeveralPluginsReplaceSlot = "FCMS1540";
    public const string NotASlot = "FCMS1541";
}

// The slots as AddFluentCms left them. The report looks at the service collection again, because a registration after
// AddFluentCms wins over the choice made there.
internal sealed class ProviderSlotRegistry(IServiceCollection services, IReadOnlyList<ProviderSlotChoice> choices)
{
    public IReadOnlyList<StartupProviderSlot> Describe(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return [.. choices.Select(choice =>
        {
            var last = services.LastOrDefault(descriptor => ProvidersBuilder.Fills(descriptor, choice.Slot));
            var chosen = last is not null && ReferenceEquals(last, choice.Descriptor);
            return new StartupProviderSlot
            {
                Slot = choice.Slot.FullName ?? choice.Slot.Name,
                Implementation = last is null ? null : ImplementationOf(last, scope.ServiceProvider),
                Source = chosen ? choice.Source : ProviderSlotSources.Services,
                Plugin = chosen ? choice.Plugin : null,
            };
        })];
    }

    // A factory is only known by what it returns; one that throws leaves the implementation unknown, and its first use throws.
    private static string? ImplementationOf(ServiceDescriptor descriptor, IServiceProvider provider)
    {
        var type = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
        if (type is null)
        {
            try
            {
                type = provider.GetService(descriptor.ServiceType)?.GetType();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return null;
            }
        }

        return type is null ? null : type.FullName ?? type.Name;
    }
}
