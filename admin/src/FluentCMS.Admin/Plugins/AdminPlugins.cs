using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Plugins;

internal enum PluginPageGate
{
    // Not a route of an admin half.
    None,

    // The half's page, which the user may see.
    Allowed,

    // The half's plugin is disabled in the site, or the half has no page of the route.
    NotFound,

    // The user lacks the page's permission.
    NoAccess,
}

// The admin halves of the app as the shell uses them (spec 2.6): each contribution counts only while its plugin is enabled
// in the site and the user holds its permission, and a half is loaded with its assembly on the first use of one of them,
// never in a site where its plugin is disabled. Loading runs the half's Configure once, into a provider of the half that
// falls back to the admin's services. A load that fails is tried again on the next use.
internal sealed class AdminPlugins(
    AdminPluginCatalog catalog,
    AdminContext context,
    IAdminAssemblyLoader loader,
    IServiceProvider services,
    AdminServiceDescriptors descriptors,
    ILogger<AdminPlugins> logger) : IAdminPluginServices
{
    private const string KeptWhole = "An admin half is loaded lazily and kept whole by the app's partial trimming, which trims only the assemblies that opt in (spec 2.6).";

    private readonly Dictionary<string, Task<LoadedHalf>> _loads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LoadedHalf> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);

    public AdminPluginCatalog Catalog => catalog;

    // The loaded halves' assemblies, which the router searches for pages besides the admin's own.
    public List<Assembly> Assemblies { get; } = [];

    // As the route's first segment is matched, whatever the case.
    public bool IsAvailable(AdminPluginManifest half) =>
        context.Plugins.Any(plugin => plugin.Enabled && string.Equals(plugin.Name, half.Name, StringComparison.OrdinalIgnoreCase));

    public bool IsLoaded(AdminPluginManifest half) => _loaded.ContainsKey(half.Name);

    // Whether the last load of the half failed, so that its page shows an error with a retry.
    public bool LoadFailed(AdminPluginManifest half) => _failed.Contains(half.Name);

    public PluginPageGate Gate(string route)
    {
        if (catalog.HalfOfRoute(route) is not { } half)
        {
            return PluginPageGate.None;
        }

        if (!IsAvailable(half) || AdminPluginRoutes.Match(half.Pages, route) is not { } page)
        {
            return PluginPageGate.NotFound;
        }

        return page.Permission is null || context.Allows(page.Permission) ? PluginPageGate.Allowed : PluginPageGate.NoAccess;
    }

    // The router's OnNavigateAsync: a page of a half loads the half, once the shell knows the site, if the user may see it.
    public async Task NavigateAsync(string route, CancellationToken cancellationToken)
    {
        if (catalog.HalfOfRoute(route) is not { } half || IsLoaded(half))
        {
            return;
        }

        await context.StartAsync();
        if (Gate(route) == PluginPageGate.Allowed)
        {
            await TryLoadAsync(half, cancellationToken);
        }
    }

    // A failure is logged and kept for the page to show; a cancelled wait is not a failure.
    public async Task<bool> TryLoadAsync(AdminPluginManifest half, CancellationToken cancellationToken)
    {
        try
        {
            await LoadAsync(half, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
#pragma warning disable CA1031 // Whatever stops a half from loading, the admin goes on without it.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    // One load per half: the callers of a load in progress wait for the same one, each for as long as it waits.
    public Task<LoadedHalf> LoadAsync(AdminPluginManifest half, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(half);
        if (_loaded.TryGetValue(half.Name, out var done))
        {
            return Task.FromResult(done);
        }

        if (!IsAvailable(half))
        {
            throw new InvalidOperationException($"The plugin {half.Name} is not enabled in the site, so its admin half is not loaded.");
        }

        if (!_loads.TryGetValue(half.Name, out var loading))
        {
            _failed.Remove(half.Name);
            loading = RunLoadAsync(half);
            if (!loading.IsCompleted)
            {
                _loads[half.Name] = loading;
            }
        }

        return loading.WaitAsync(cancellationToken);
    }

    public IServiceProvider ProviderOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _loaded.Values.FirstOrDefault(half => half.Assembly == type.Assembly)?.Provider
            ?? throw new InvalidOperationException($"{type.FullName} is not a type of a loaded admin half.");
    }

    public async ValueTask<TService?> FindAsync<TService>(CancellationToken cancellationToken = default)
        where TService : class
    {
        var name = typeof(TService).FullName;
        foreach (var half in catalog.Halves.Where(IsAvailable))
        {
            if (half.Services.FirstOrDefault(service => service.Service == name) is { } entry)
            {
                var loaded = await LoadAsync(half, cancellationToken);
                return (TService)loaded.Service(typeof(TService), entry.Type);
            }
        }

        return null;
    }

    // The field editors, list cells and filters of the halves, which the registries load on first use.
    public void Declare(FieldEditorRegistry editors)
    {
        ArgumentNullException.ThrowIfNull(editors);
        foreach (var half in catalog.Halves)
        {
            foreach (var editor in half.FieldEditors)
            {
                editors.AddLazy(editor.FieldType, () => IsAvailable(half), cancellationToken => TypeAsync(half, editor.Type, cancellationToken));
            }
        }
    }

    public void Declare(ListCellRegistry cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        foreach (var half in catalog.Halves)
        {
            foreach (var cell in half.ListCells)
            {
                cells.AddLazy(cell.FieldType, () => IsAvailable(half), cancellationToken => TypeAsync(half, cell.Type, cancellationToken));
            }
        }
    }

    public void Declare(ListFilterRegistry filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        foreach (var half in catalog.Halves)
        {
            foreach (var fieldType in half.FieldEditors.Concat(half.ListCells).Select(component => component.FieldType).Distinct(StringComparer.Ordinal))
            {
                filters.AddLazy(fieldType, () => IsAvailable(half), cancellationToken => LoadAsync(half, cancellationToken));
            }
        }
    }

    private async Task<Type> TypeAsync(AdminPluginManifest half, string typeName, CancellationToken cancellationToken) =>
        (await LoadAsync(half, cancellationToken)).Type(typeName);

    private async Task<LoadedHalf> RunLoadAsync(AdminPluginManifest half)
    {
        try
        {
            var assembly = await loader.LoadAsync(half.Assembly, CancellationToken.None);
            var loaded = Configure(half, assembly);
            _loaded[half.Name] = loaded;
            Assemblies.Add(assembly);
            return loaded;
        }
        catch (Exception exception)
        {
            PluginLog.LoadFailed(logger, half.Name, exception);
            _failed.Add(half.Name);
            throw;
        }
        finally
        {
            _loads.Remove(half.Name);
        }
    }

    // The plugin's class is the one the assembly names; its parameterless constructor is kept by the attribute's annotation.
    private LoadedHalf Configure(AdminPluginManifest half, Assembly assembly)
    {
        var pluginType = assembly.GetCustomAttribute<FluentCmsAdminPluginAttribute>()?.PluginType
            ?? throw new InvalidOperationException($"{assembly.GetName().Name} names no plugin.");
        var name = pluginType.GetCustomAttribute<FluentCmsAdminPluginAttribute>()?.Name;
        if (!string.Equals(name, half.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{assembly.GetName().Name} is the admin half of '{name}', and its manifest names '{half.Name}'.");
        }

        var plugin = Activator.CreateInstance(pluginType) as IAdminPlugin
            ?? throw new InvalidOperationException($"{pluginType.FullName} does not implement IAdminPlugin.");
        var collection = new ServiceCollection();
        plugin.Configure(new AdminPluginBuilder(half.Name, collection, services.GetRequiredService<ListFilterRegistry>()));
        descriptors.FallBackTo(collection, services);
        return new LoadedHalf(assembly, collection.BuildServiceProvider());
    }

    internal sealed class LoadedHalf(Assembly assembly, IServiceProvider provider)
    {
        private readonly Dictionary<Type, object> _services = [];

        public Assembly Assembly => assembly;

        public IServiceProvider Provider => provider;

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = KeptWhole)]
        [UnconditionalSuppressMessage("Trimming", "IL2073", Justification = KeptWhole)]
        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        public Type Type(string name) => assembly.GetType(name, throwOnError: true)!;

        // The half's own registration of the service when it has one, else one instance of the class its manifest names,
        // made by the half's provider.
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = KeptWhole)]
        [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = KeptWhole)]
        public object Service(Type service, string implementation)
        {
            if (!_services.TryGetValue(service, out var instance))
            {
                instance = provider.GetService(service) ?? ActivatorUtilities.CreateInstance(provider, assembly.GetType(implementation, throwOnError: true)!);
                if (!service.IsInstanceOfType(instance))
                {
                    throw new InvalidOperationException($"{implementation} does not implement {service.FullName}.");
                }

                _services[service] = instance;
            }

            return instance;
        }
    }

    private sealed class AdminPluginBuilder(string name, IServiceCollection services, ListFilterRegistry filters) : IAdminPluginBuilder
    {
        public string Name => name;

        public IServiceCollection Services => services;

        public ListFilterRegistry Filters => filters;
    }
}
