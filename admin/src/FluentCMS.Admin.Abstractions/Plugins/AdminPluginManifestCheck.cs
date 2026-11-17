using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Lists;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Plugins;

/// <summary>
/// The test helper of an admin half: it reads the contributions the attributes of the half's assembly declare and compares
/// them with the hand-written <c>fluentcms-admin.json</c>, so the half's tests fail when the two disagree. It reflects over
/// the whole assembly, so it is for tests and build steps and not for the app.
/// </summary>
public static class AdminPluginManifestCheck
{
    private const string Reflects = "Reads every type of the assembly; for tests and build steps, not for a trimmed app.";

    /// <summary>The manifest the attributes of an assembly declare, its contributions in the order of their classes' names.</summary>
    /// <param name="assembly">The assembly of the half.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="InvalidOperationException">The assembly names no plugin, or its plugin has no name.</exception>
    [RequiresUnreferencedCode(Reflects)]
    public static AdminPluginManifest FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var problems = new List<string>();
        return Describe(assembly, problems) ?? throw new InvalidOperationException(problems[0]);
    }

    /// <summary>
    /// The differences between a manifest file and the attributes of an assembly: a contribution missing from either, one
    /// whose route, field type, permission or other member differs, and a declaration the admin could not use, such as a
    /// page without its <c>@page</c> route.
    /// </summary>
    /// <param name="assembly">The assembly of the half.</param>
    /// <param name="manifestJson">The text of the half's <c>fluentcms-admin.json</c>.</param>
    /// <returns>One sentence per difference; none when the two agree.</returns>
    [RequiresUnreferencedCode(Reflects)]
    public static IReadOnlyList<string> Differences(Assembly assembly, string manifestJson)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var problems = new List<string>();
        var declared = Describe(assembly, problems);
        if (declared is not null)
        {
            try
            {
                problems.AddRange(Compare(AdminPluginManifestJson.Read(manifestJson), declared));
            }
            catch (JsonException unreadable)
            {
                problems.Add($"the manifest cannot be read: {unreadable.Message}");
            }
        }

        return problems;
    }

    /// <summary>The differences between a manifest file and the manifest the attributes of its assembly declare.</summary>
    /// <param name="manifest">The manifest of the file.</param>
    /// <param name="declared">The manifest of the assembly, from <see cref="FromAssembly"/>.</param>
    /// <returns>One sentence per difference; none when the two agree.</returns>
    public static IReadOnlyList<string> Compare(AdminPluginManifest manifest, AdminPluginManifest declared)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(declared);
        var differences = new List<string>();

        if (manifest.ManifestVersion != AdminPluginManifest.CurrentManifestVersion)
        {
            differences.Add($"manifestVersion is {manifest.ManifestVersion} in the manifest and this contract reads {AdminPluginManifest.CurrentManifestVersion}");
        }

        CompareMember(differences, "name", declared.Name, manifest.Name);
        CompareMember(differences, "version", declared.Version, manifest.Version);
        CompareMember(differences, "adminRange", declared.AdminRange, manifest.AdminRange);
        CompareMember(differences, "assembly", declared.Assembly, manifest.Assembly);
        CompareItems(differences, Items(declared), Items(manifest));
        return differences;
    }

    /// <summary>Fails when a manifest file and the attributes of an assembly disagree.</summary>
    /// <param name="assembly">The assembly of the half.</param>
    /// <param name="manifestJson">The text of the half's <c>fluentcms-admin.json</c>.</param>
    /// <exception cref="InvalidOperationException">The two disagree; the message names each difference on a line of its own.</exception>
    [RequiresUnreferencedCode(Reflects)]
    public static void Verify(Assembly assembly, string manifestJson)
    {
        var differences = Differences(assembly, manifestJson);
        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"{AdminPluginManifest.FileName} and the attributes of {assembly.GetName().Name} disagree:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
        }
    }

    /// <summary>Fails when a manifest file and the attributes of an assembly disagree.</summary>
    /// <param name="assembly">The assembly of the half.</param>
    /// <param name="manifestPath">The path of the half's <c>fluentcms-admin.json</c>.</param>
    /// <exception cref="InvalidOperationException">The two disagree; the message names each difference on a line of its own.</exception>
    [RequiresUnreferencedCode(Reflects)]
    public static void VerifyFile(Assembly assembly, string manifestPath) => Verify(assembly, File.ReadAllText(manifestPath));

    [RequiresUnreferencedCode(Reflects)]
    private static AdminPluginManifest? Describe(Assembly assembly, List<string> problems)
    {
        var assemblyName = assembly.GetName().Name ?? "";
        var pluginType = assembly.GetCustomAttribute<FluentCmsAdminPluginAttribute>()?.PluginType;
        if (pluginType is null)
        {
            problems.Add($"{assemblyName} names no plugin: add [assembly: FluentCmsAdminPlugin(typeof(...))]");
            return null;
        }

        var plugin = pluginType.GetCustomAttribute<FluentCmsAdminPluginAttribute>();
        if (plugin?.Name is null)
        {
            problems.Add($"{pluginType.FullName} has no name: add [FluentCmsAdminPlugin(\"...\")] to the class");
            return null;
        }

        if (!typeof(IAdminPlugin).IsAssignableFrom(pluginType))
        {
            problems.Add($"{pluginType.FullName} does not implement IAdminPlugin");
        }

        var pages = new List<AdminManifestPage>();
        var menuItems = new List<AdminManifestMenuItem>();
        var settingsPages = new List<AdminManifestSettingsPage>();
        var fieldEditors = new List<AdminManifestFieldComponent>();
        var listCells = new List<AdminManifestFieldComponent>();
        var entryActions = new List<AdminManifestAction>();
        var bulkActions = new List<AdminManifestAction>();
        var fieldActions = new List<AdminManifestFieldAction>();
        var widgets = new List<AdminManifestWidget>();
        var services = new List<AdminManifestService>();

        foreach (var type in assembly.GetTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var name = type.FullName ?? type.Name;
            var typePages = type.GetCustomAttributes<AdminPageAttribute>().ToList();
            var routes = type.GetCustomAttributes<RouteAttribute>().Select(route => route.Template).ToHashSet(StringComparer.Ordinal);
            foreach (var page in typePages)
            {
                if (!routes.Contains(page.Route))
                {
                    problems.Add($"page '{page.Route}' of {name}: the component has no @page of that route, so the router cannot find it");
                }

                pages.Add(new AdminManifestPage { Type = name, Route = page.Route, Permission = page.Permission });
            }

            if (type.GetCustomAttribute<AdminMenuItemAttribute>() is { } item)
            {
                var page = item.Route is null
                    ? typePages.Count == 1 ? typePages[0] : null
                    : typePages.Find(candidate => candidate.Route == item.Route);
                if (page is null)
                {
                    problems.Add(item.Route is not null
                        ? $"menu item '{item.Label}' of {name}: the component declares no [AdminPage] of its Route '{item.Route}'"
                        : typePages.Count > 1
                            ? $"menu item '{item.Label}' of {name}: the component has several pages, so the item must name its Route"
                            : $"menu item '{item.Label}' of {name}: the component declares no [AdminPage] for it to link to");
                }
                else
                {
                    menuItems.Add(new AdminManifestMenuItem
                    {
                        Type = name,
                        Label = item.Label,
                        Route = page.Route,
                        Section = item.Section,
                        Icon = item.Icon,
                        Order = item.Order,
                        Permission = page.Permission,
                    });
                }
            }

            if (type.GetCustomAttribute<SettingsPageAttribute>() is { } settings)
            {
                Require<IComponent>(type, $"settings page '{settings.Area}'", problems);
                settingsPages.Add(new AdminManifestSettingsPage { Type = name, Area = settings.Area });
            }

            foreach (var editor in type.GetCustomAttributes<FieldEditorAttribute>())
            {
                Require<FieldEditorBase>(type, $"field editor '{editor.FieldType}'", problems);
                fieldEditors.Add(new AdminManifestFieldComponent { Type = name, FieldType = editor.FieldType });
            }

            foreach (var cell in type.GetCustomAttributes<ListCellAttribute>())
            {
                Require<ListCellBase>(type, $"list cell '{cell.FieldType}'", problems);
                listCells.Add(new AdminManifestFieldComponent { Type = name, FieldType = cell.FieldType });
            }

            if (type.GetCustomAttribute<EntryActionAttribute>() is { } entryAction)
            {
                Require<EntryActionBase>(type, $"entry action '{entryAction.Name}'", problems);
                entryActions.Add(new AdminManifestAction { Type = name, Name = entryAction.Name, ContentTypes = entryAction.ContentTypes, Permission = entryAction.Permission });
            }

            if (type.GetCustomAttribute<BulkActionAttribute>() is { } bulkAction)
            {
                Require<BulkActionBase>(type, $"bulk action '{bulkAction.Name}'", problems);
                bulkActions.Add(new AdminManifestAction { Type = name, Name = bulkAction.Name, ContentTypes = bulkAction.ContentTypes, Permission = bulkAction.Permission });
            }

            if (type.GetCustomAttribute<FieldActionAttribute>() is { } fieldAction)
            {
                Require<FieldActionBase>(type, $"field action '{fieldAction.Name}'", problems);
                fieldActions.Add(new AdminManifestFieldAction { Type = name, Name = fieldAction.Name, FieldTypes = fieldAction.FieldTypes, Permission = fieldAction.Permission });
            }

            if (type.GetCustomAttribute<DashboardWidgetAttribute>() is { } widget)
            {
                Require<IComponent>(type, $"widget '{widget.Title}'", problems);
                widgets.Add(new AdminManifestWidget { Type = name, Title = widget.Title, Size = widget.Size, Permission = widget.Permission, Role = widget.Role });
            }

            foreach (var service in type.GetCustomAttributes<AdminServiceAttribute>())
            {
                var serviceName = service.ServiceType.FullName ?? service.ServiceType.Name;
                if (!service.ServiceType.IsAssignableFrom(type))
                {
                    problems.Add($"service '{serviceName}' of {name}: the class does not implement it");
                }

                services.Add(new AdminManifestService { Type = name, Service = serviceName });
            }
        }

        var manifest = new AdminPluginManifest
        {
            Name = plugin.Name,
            Version = plugin.Version,
            AdminRange = plugin.AdminRange,
            Assembly = assemblyName,
            Pages = pages,
            MenuItems = menuItems,
            SettingsPages = settingsPages,
            FieldEditors = fieldEditors,
            ListCells = listCells,
            EntryActions = entryActions,
            BulkActions = bulkActions,
            FieldActions = fieldActions,
            Widgets = widgets,
            Services = services,
        };

        // The shell finds a contribution by its kind and its name, so two classes cannot share one.
        problems.AddRange(Items(manifest)
            .Where(item => item.Kind is not ("menu item" or "widget"))
            .GroupBy(item => (item.Kind, item.Key))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Kind} {Quote(group.Key.Key)} is declared more than once: by {string.Join(" and ", group.Select(item => item.Type))}"));
        return manifest;
    }

    private static void Require<TBase>(Type type, string contribution, List<string> problems)
    {
        if (!typeof(TBase).IsAssignableFrom(type))
        {
            problems.Add($"{contribution} of {type.FullName}: the class does not derive from {typeof(TBase).Name}");
        }
    }

    // Every contribution of a manifest in one shape: its kind, its class, the member that names it, and its other members.
    private static List<Item> Items(AdminPluginManifest manifest) =>
    [
        .. manifest.Pages.Select(page => new Item("page", page.Type, "route", page.Route, [("permission", page.Permission)])),
        .. manifest.MenuItems.Select(item => new Item("menu item", item.Type, "label", item.Label,
        [
            ("route", item.Route), ("section", Text(item.Section)), ("icon", item.Icon), ("order", Text(item.Order)), ("permission", item.Permission),
        ])),
        .. manifest.SettingsPages.Select(page => new Item("settings page", page.Type, "area", page.Area, [])),
        .. manifest.FieldEditors.Select(editor => new Item("field editor", editor.Type, "fieldType", editor.FieldType, [])),
        .. manifest.ListCells.Select(cell => new Item("list cell", cell.Type, "fieldType", cell.FieldType, [])),
        .. manifest.EntryActions.Select(action => new Item("entry action", action.Type, "name", action.Name,
            [("contentTypes", Text(action.ContentTypes)), ("permission", action.Permission)])),
        .. manifest.BulkActions.Select(action => new Item("bulk action", action.Type, "name", action.Name,
            [("contentTypes", Text(action.ContentTypes)), ("permission", action.Permission)])),
        .. manifest.FieldActions.Select(action => new Item("field action", action.Type, "name", action.Name,
            [("fieldTypes", Text(action.FieldTypes)), ("permission", action.Permission)])),
        .. manifest.Widgets.Select(widget => new Item("widget", widget.Type, "title", widget.Title,
            [("size", Text(widget.Size)), ("permission", widget.Permission), ("role", widget.Role)])),
        .. manifest.Services.Select(service => new Item("service", service.Type, "service", service.Service, [])),
    ];

    private static void CompareItems(List<string> differences, List<Item> declared, List<Item> manifest)
    {
        // The same kind, class and name: the other members are compared.
        foreach (var item in declared.ToList())
        {
            if (manifest.Find(candidate => candidate.Kind == item.Kind && candidate.Type == item.Type && candidate.Key == item.Key) is { } match)
            {
                CompareMembers(differences, item, match);
                declared.Remove(item);
                manifest.Remove(match);
            }
        }

        // The same kind and class under another name: the contribution was renamed on one side, such as a page's route.
        foreach (var item in declared.ToList())
        {
            if (manifest.Find(candidate => candidate.Kind == item.Kind && candidate.Type == item.Type) is { } match)
            {
                differences.Add($"{item.Kind} of {item.Type}: {item.KeyName} is {Quote(item.Key)} in the assembly and {Quote(match.Key)} in the manifest");
                CompareMembers(differences, item, match);
                declared.Remove(item);
                manifest.Remove(match);
            }
        }

        differences.AddRange(declared.Select(item => $"{item.Kind} {Quote(item.Key)} of {item.Type}: declared by the assembly and missing from the manifest"));
        differences.AddRange(manifest.Select(item => $"{item.Kind} {Quote(item.Key)} of {item.Type}: in the manifest and not declared by the assembly"));
    }

    private static void CompareMembers(List<string> differences, Item declared, Item manifest)
    {
        foreach (var ((name, value), (_, other)) in declared.Members.Zip(manifest.Members))
        {
            if (!string.Equals(value, other, StringComparison.Ordinal))
            {
                differences.Add($"{declared.Kind} {Quote(declared.Key)} of {declared.Type}: {name} is {Quote(value)} in the assembly and {Quote(other)} in the manifest");
            }
        }
    }

    private static void CompareMember(List<string> differences, string name, string? declared, string? manifest)
    {
        if (!string.Equals(declared, manifest, StringComparison.Ordinal))
        {
            differences.Add($"{name} is {Quote(declared)} in the assembly and {Quote(manifest)} in the manifest");
        }
    }

    private static string Quote(string? value) => value is null ? "not set" : $"'{value}'";

    private static string Text(IReadOnlyList<string> values) => string.Join(", ", values.Order(StringComparer.Ordinal));

    private static string Text(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Text(AdminSection section) => section switch
    {
        AdminSection.Content => "content",
        AdminSection.Media => "media",
        AdminSection.Administration => "administration",
        AdminSection.Settings => "settings",
        _ => Text((int)section),
    };

    private static string Text(WidgetSize size) => size switch
    {
        WidgetSize.Small => "small",
        WidgetSize.Medium => "medium",
        WidgetSize.Wide => "wide",
        _ => Text((int)size),
    };

    private sealed record Item(string Kind, string Type, string KeyName, string Key, (string Name, string? Value)[] Members);
}
