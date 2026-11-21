using FluentCMS.Admin.Plugins;

namespace FluentCMS.Admin.Shell;

// The sections of the sidebar after the content types, in this order (spec 5.1).
internal enum AdminMenuSection
{
    Content,
    Media,
    Administration,
    Settings,
}

// A page the sidebar links to besides the content types: an area of core's administration pages, or a menu item of a
// plugin's admin half. An area shows while the user may open it. An item shows only while its plugin is enabled in the site
// and the user holds its permission, if it names one, for the instance when its page is instance-only. The label is a key of
// the admin's resources, or for a half the texts of its manifest's item, since the half's resources are not loaded before
// the half is.
internal sealed record AdminMenuEntry(
    string Route,
    string LabelKey,
    AdminMenuSection Section,
    string? Permission = null,
    string? Plugin = null,
    AdminManifestMenuItem? Item = null,
    AdministrationArea? Area = null)
{
    public int Order => Area?.Order ?? Item?.Order ?? 0;
}

// The sidebar links only to pages that exist: the areas of core's administration pages, and the menu items of the app's
// admin halves.
internal sealed class AdminMenu
{
    // The entries of core's administration pages, one per area, from the area's own declaration.
    public static IReadOnlyList<AdminMenuEntry> Core { get; } =
        [.. AdministrationAreas.All.Select(area => new AdminMenuEntry(area.Route, area.LabelKey, area.Section, area.Permission, Area: area))];

    public IReadOnlyList<AdminMenuEntry> Entries { get; init; } = Core;
}
