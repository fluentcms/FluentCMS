namespace FluentCMS.Admin.Shell;

// An area of core's administration pages (spec 4.15, 4.16): the route its pages are at or below, the label of its sidebar
// entry, a key of the admin's resources, and the permission its pages need, for the instance when the area is instance-only,
// since every call of such an area answers 403 to a caller who holds the permission in a site only. The sidebar entry and
// the gate of the route read this one declaration. A permission with a star stands for any one name in its place.
internal sealed record AdministrationArea(string Route, string LabelKey, AdminMenuSection Section, int Order, string Permission, bool InstanceOnly = false);

// The areas whose pages are in FluentCMS.Admin.Administration, which the app loads lazily on the first visit of one of their
// routes (spec 2.6), so the shell knows them before that assembly is loaded. A page there adds no entry here: it takes a
// route below its area's.
internal static class AdministrationAreas
{
    public const string AssemblyName = "FluentCMS.Admin.Administration";

    // The settings menu shows the areas the caller may manage, each with a permission of its own (spec 4.16).
    public const string AnySettingsArea = "settings.*.manage";

    public static IReadOnlyList<AdministrationArea> All { get; } =
    [
        new("users", "Administration.Menu.Users", AdminMenuSection.Administration, 10, "users.manage", InstanceOnly: true),
        new("roles", "Administration.Menu.Roles", AdminMenuSection.Administration, 20, "roles.manage"),
        new("api-keys", "Administration.Menu.ApiKeys", AdminMenuSection.Administration, 30, "apikeys.manage"),
        new("members", "Administration.Menu.Members", AdminMenuSection.Administration, 40, "members.manage", InstanceOnly: true),
        new("plugins", "Administration.Menu.Plugins", AdminMenuSection.Administration, 50, "plugins.manage"),
        new("jobs", "Administration.Menu.Jobs", AdminMenuSection.Administration, 60, "jobs.manage", InstanceOnly: true),
        new("system/diagnostics", "Administration.Menu.Diagnostics", AdminMenuSection.Administration, 70, "system.read", InstanceOnly: true),
        new("settings", "Administration.Menu.Settings", AdminMenuSection.Settings, 10, AnySettingsArea),
        new("sites", "Administration.Menu.Sites", AdminMenuSection.Settings, 20, "sites.manage"),
    ];

    // The area a route relative to the app's base is in: the area's own route, or one below it, in any case, as the router
    // matches.
    public static AdministrationArea? OfRoute(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var end = route.IndexOfAny(['?', '#']);
        var path = Uri.UnescapeDataString(end < 0 ? route : route[..end]).Trim('/');
        return All.FirstOrDefault(area => path.StartsWith(area.Route, StringComparison.OrdinalIgnoreCase)
            && (path.Length == area.Route.Length || path[area.Route.Length] == '/'));
    }
}
