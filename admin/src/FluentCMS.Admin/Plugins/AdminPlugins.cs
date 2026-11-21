using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
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

    // The user lacks the page's permission, or holds it in a site only for a page that needs it for the instance.
    NoAccess,
}

// A bulk action of an admin half, as its manifest declares it.
internal sealed record PluginBulkAction(AdminPluginManifest Half, AdminManifestAction Declared);

// A field action of an admin half, as its manifest declares it.
internal sealed record PluginFieldAction(AdminPluginManifest Half, AdminManifestFieldAction Declared);

// A dashboard widget of an admin half, as its manifest declares it, at its position in the manifest's list.
internal sealed record PluginWidget(AdminPluginManifest Half, AdminManifestWidget Widget, int Position);

// The admin halves of the app as the shell uses them (spec 2.6): each contribution counts only while its plugin is enabled
// in the site and the user holds its permission, and a half is loaded with its assembly on the first use of one of them,
// never in a site where its plugin is disabled. Loading runs the half's Configure once, into a provider of the half that
// falls back to the admin's services. A load that fails is tried again on the next use. The router asks it about core's
// administration pages too, which load the same way, behind the gate of their area (spec 4.15).
internal sealed class AdminPlugins(
    AdminPluginCatalog catalog,
    AdminContext context,
    AdministrationAssembly administration,
    IAdminAssemblyLoader loader,
    IServiceProvider services,
    AdminServiceDescriptors descriptors,
    ILogger<AdminPlugins> logger) : IAdminPluginServices
{
    // Any base does: only the query string of a route is read for its locale.
    private static readonly Uri RouteBase = new("https://admin.invalid/");

    private const string KeptWhole = "An admin half is loaded lazily and kept whole by the app's partial trimming, which trims only the assemblies that opt in (spec 2.6).";

    // Keyed by the plugin's name, which GET /plugins gives in lower-case kebab case and is compared as it is.
    private readonly Dictionary<string, Task<LoadedHalf>> _loads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LoadedHalf> _loaded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Exception> _failed = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string?> _problems = [];

    // The half's route the router last navigated to and the gate it answered then.
    private (string Route, PluginPageGate Gate)? _navigated;

    public AdminPluginCatalog Catalog => catalog;

    // The assemblies of the loaded halves and of the administration pages, once loaded, which the router searches for pages
    // besides the admin's own.
    public List<Assembly> Assemblies { get; } = [];

    public bool IsAvailable(AdminPluginManifest half) => context.PluginEnabled(half.Name);

    public bool IsLoaded(AdminPluginManifest half) => _loaded.ContainsKey(half.Name);

    // Whether the last load of the half failed, so that its page shows an error with a retry. The failure is kept, and every
    // use of the half fails at once, until the next navigation tries again.
    public bool LoadFailed(AdminPluginManifest half) => _failed.ContainsKey(half.Name);

    // Whether a contribution of a half that declares a permission, or none, may be shown: its plugin is enabled in the site and
    // the user holds the permission. The entry point of every contribution the shell draws from a manifest, such as a widget
    // (A-22), an action (A5) or a settings page (A6), with ComponentAsync to load its class.
    public bool Shows(AdminPluginManifest half, string? permission)
    {
        ArgumentNullException.ThrowIfNull(half);
        return IsAvailable(half) && (permission is null || context.Allows(permission));
    }

    // The widgets of the home page the user may see, by the plugin's name and then in the order of its manifest: those whose
    // plugin is enabled in the site, whose permission the user holds and whose role it has, for each that is set.
    public IReadOnlyList<PluginWidget> Widgets() =>
    [
        .. catalog.Halves.OrderBy(half => half.Name, StringComparer.Ordinal).SelectMany(half => half.Widgets
            .Select((widget, position) => new PluginWidget(half, widget, position))
            .Where(item => Shows(half, item.Widget.Permission) && (item.Widget.Role is null || context.HasRole(item.Widget.Role)))),
    ];

    // The bulk actions of a content type the user may see, by the plugin's name and then in the order of its manifest.
    public IReadOnlyList<PluginBulkAction> BulkActions(string contentType) =>
    [
        .. catalog.Halves.OrderBy(half => half.Name, StringComparer.Ordinal).SelectMany(half => half.BulkActions
            .Where(action => Shows(half, action.Permission) && (action.ContentTypes.Count == 0 || action.ContentTypes.Contains(contentType, StringComparer.Ordinal)))
            .Select(action => new PluginBulkAction(half, action))),
    ];

    // A new instance of the action, made by the provider of its half, which loads first. Call it only for what BulkActions answers.
    public async Task<BulkActionBase> BulkActionAsync(PluginBulkAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var loaded = await LoadAsync(action.Half, cancellationToken);
        return loaded.Create(action.Declared.Type) as BulkActionBase
            ?? throw new InvalidOperationException($"{action.Declared.Type} does not derive from BulkActionBase.");
    }

    // The field actions of a field type the user may see, by the plugin's name and then in the order of its manifest; the form
    // narrows them by their FieldsFrom.
    public IReadOnlyList<PluginFieldAction> FieldActions(string fieldType) =>
    [
        .. catalog.Halves.OrderBy(half => half.Name, StringComparer.Ordinal).SelectMany(half => half.FieldActions
            .Where(action => Shows(half, action.Permission) && (action.FieldTypes.Count == 0 || action.FieldTypes.Contains(fieldType, StringComparer.Ordinal)))
            .Select(action => new PluginFieldAction(half, action))),
    ];

    // A new instance of the action, made by the provider of its half, which loads first. Call it only for what FieldActions answers.
    public async Task<FieldActionBase> FieldActionAsync(PluginFieldAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var loaded = await LoadAsync(action.Half, cancellationToken);
        return loaded.Create(action.Declared.Type) as FieldActionBase
            ?? throw new InvalidOperationException($"{action.Declared.Type} does not derive from FieldActionBase.");
    }

    // The class of a contribution, by the full name its manifest gives, once its half is loaded. Call it only for what Shows
    // allows; it throws for a half whose plugin is disabled or that failed to load. The app's partial trimming keeps the
    // half's types whole.
    public async Task<Type> ComponentAsync(AdminPluginManifest half, string typeName, CancellationToken cancellationToken) =>
        (await LoadAsync(half, cancellationToken)).Type(typeName);

    // The gate of a page the router found, from the route data the router renders it with, and never from the address: the
    // router may render the page of the address before while the address is already the next one, and the page of the
    // address before that address's OnNavigateAsync has run. A page of the administration assembly is gated by its area,
    // the one area all its routes are in, so that a page outside every area, or across two, has no page. A page of a loaded
    // half is gated by the manifest's pages of its class whose routes the route data agrees with (PageRoutes), so that a
    // route of the class that the manifest does not declare has no page, and a route data that two of them agree with
    // requires what each requires. A page of the admin's own is not gated here.
    public PluginPageGate Gate(RouteData routeData)
    {
        ArgumentNullException.ThrowIfNull(routeData);
        if (routeData.PageType.Assembly == administration.Loaded)
        {
            return AdministrationAreas.OfPage(routeData.PageType) is not { } area ? PluginPageGate.NotFound
                : context.Allows(area) ? PluginPageGate.Allowed : PluginPageGate.NoAccess;
        }

        if (_loaded.Values.FirstOrDefault(loaded => loaded.Assembly == routeData.PageType.Assembly) is not { } owner)
        {
            return PluginPageGate.None;
        }

        var declared = owner.Manifest.Pages.Where(page => page.Type == routeData.PageType.FullName).ToList();
        var matched = PageRoutes.Of(routeData).Select(template => declared.FirstOrDefault(page => page.Route == template)).ToList();
        if (!IsAvailable(owner.Manifest) || matched.Count == 0 || matched.Contains(null))
        {
            return PluginPageGate.NotFound;
        }

        return matched.All(page => context.Allows(page!.Permission, page.InstanceOnly)) ? PluginPageGate.Allowed : PluginPageGate.NoAccess;
    }

    public PluginPageGate Gate(string route)
    {
        if (AdministrationAreas.OfRoute(route) is { } area)
        {
            return context.Allows(area) ? PluginPageGate.Allowed : PluginPageGate.NoAccess;
        }

        if (catalog.HalfOfRoute(route) is not { } half)
        {
            return PluginPageGate.None;
        }

        if (!IsAvailable(half) || AdminPluginRoutes.Match(half.Pages, route) is not { } page)
        {
            return PluginPageGate.NotFound;
        }

        return context.Allows(page.Permission, page.InstanceOnly) ? PluginPageGate.Allowed : PluginPageGate.NoAccess;
    }

    // Whether the last load of what shows the route failed: the half of the route, or the administration assembly.
    public bool LoadFailed(string route) =>
        AdministrationAreas.OfRoute(route) is not null ? administration.Failed : catalog.HalfOfRoute(route) is { } half && LoadFailed(half);

    // The router's OnNavigateAsync: a page of a half loads the half, and a page of an area of administration its assembly,
    // once the shell knows the site, if the user may see it. A navigation tries a failed load again. What the user sees is
    // decided by the gate of the page the router renders, which does not trust that a route is rendered only once this ends.
    public async Task NavigateAsync(string route, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        _failed.Clear();
        administration.TryAgain();
        _navigated = null;
        if (AdministrationAreas.OfRoute(route) is not null)
        {
            if (administration.Loaded is null)
            {
                await context.StartAsync();
            }

            if (!await UseLocaleOfAsync(route, cancellationToken))
            {
                return;
            }

            var allowed = Gate(route);
            _navigated = (route, allowed);
            if (allowed == PluginPageGate.Allowed && administration.Loaded is null)
            {
                await administration.TryLoadAsync(cancellationToken);
            }

            // Also the assembly of a load that ended after the navigation that started it was overtaken.
            if (administration.Loaded is { } assembly && !Assemblies.Contains(assembly))
            {
                Assemblies.Add(assembly);
            }

            return;
        }

        if (catalog.HalfOfRoute(route) is not { } half)
        {
            return;
        }

        if (!IsLoaded(half))
        {
            await context.StartAsync();
        }

        if (!await UseLocaleOfAsync(route, cancellationToken))
        {
            return;
        }

        var gate = Gate(route);
        _navigated = (route, gate);
        if (gate == PluginPageGate.Allowed && !IsLoaded(half))
        {
            await TryLoadAsync(half, cancellationToken);
        }
    }

    // The server narrows the access by the content locale, so the gate of a route that names another locale waits for the
    // access of that locale before it decides whether to load what the route needs. The router may still render the route's
    // page meanwhile, as when a navigation it cancelled ends; the route view then holds the page before, since the access
    // the shell holds is not that of the locale the address names. A content page sets its locale itself before it reads.
    // False for a navigation the router cancelled, as by a later one, which then does nothing more: its wait may end after
    // the later navigation's, whose gate and load are the ones that count.
    private async Task<bool> UseLocaleOfAsync(string route, CancellationToken cancellationToken)
    {
        try
        {
            await context.UseLocaleAsync(ContentRoutes.LocaleOf(new Uri(RouteBase, route))).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return !cancellationToken.IsCancellationRequested;
    }

    // Whether a half's route that the last navigation answered as not found or no access is allowed now, as when the shell's
    // first reads failed or a site switch was in flight then, so that the not-found page navigates to it again. The new
    // navigation records the route as allowed, so it is navigated to again once.
    public bool AllowedSinceNavigation(string route) =>
        _navigated is { } navigated && navigated.Gate != PluginPageGate.Allowed && string.Equals(navigated.Route, route, StringComparison.Ordinal)
            && Gate(route) == PluginPageGate.Allowed;

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

        if (_failed.TryGetValue(half.Name, out var failure))
        {
            return Task.FromException<LoadedHalf>(failure);
        }

        if (!_loads.TryGetValue(half.Name, out var loading))
        {
            loading = RunLoadAsync(half);
            if (!loading.IsCompleted)
            {
                _loads[half.Name] = loading;
            }
        }

        return loading.WaitAsync(cancellationToken);
    }

    // Why the renderer cannot create a component of a loaded half, which no error boundary would catch, checked once per class
    // and logged; null when it can, and for a class of no loaded half.
    public string? ProblemOf(Type component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (_problems.TryGetValue(component, out var problem))
        {
            return problem;
        }

        if (_loaded.Values.FirstOrDefault(half => half.Assembly == component.Assembly) is not { } owner)
        {
            return null;
        }

        problem = PluginComponents.Problem(component, services);
        if (problem is not null)
        {
            PluginLog.ComponentNotCreated(logger, owner.Manifest.Name, component.FullName ?? component.Name, problem);
        }

        _problems[component] = problem;
        return problem;
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
                LoadedHalf loaded;
                try
                {
                    loaded = await LoadAsync(half, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw new InvalidOperationException($"The admin half {half.Name}, which provides {name}, could not be loaded.", exception);
                }

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
                editors.AddLazy(editor.FieldType, () => IsAvailable(half), cancellationToken => CreatableAsync(half, editor.Type, cancellationToken));
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
                cells.AddLazy(cell.FieldType, () => IsAvailable(half), cancellationToken => CreatableAsync(half, cell.Type, cancellationToken), cell.Populate);
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

    // An editor or a cell the renderer cannot create fails its load, so that the registry answers the fallback in its place.
    private async Task<Type> CreatableAsync(AdminPluginManifest half, string typeName, CancellationToken cancellationToken)
    {
        var type = await ComponentAsync(half, typeName, cancellationToken);
        return ProblemOf(type) is { } problem ? throw new InvalidOperationException(problem) : type;
    }

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
            _failed[half.Name] = exception;
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
        return new LoadedHalf(half, assembly, collection.BuildServiceProvider());
    }

    internal sealed class LoadedHalf(AdminPluginManifest manifest, Assembly assembly, IServiceProvider provider)
    {
        private readonly Dictionary<Type, object> _services = [];

        public AdminPluginManifest Manifest => manifest;

        public Assembly Assembly => assembly;

        public IServiceProvider Provider => provider;

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = KeptWhole)]
        [UnconditionalSuppressMessage("Trimming", "IL2073", Justification = KeptWhole)]
        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        public Type Type(string name) => assembly.GetType(name, throwOnError: true)!;

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = KeptWhole)]
        [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = KeptWhole)]
        public object Create(string implementation) => ActivatorUtilities.CreateInstance(provider, assembly.GetType(implementation, throwOnError: true)!);

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
