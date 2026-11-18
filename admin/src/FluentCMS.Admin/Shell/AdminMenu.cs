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

// A page the sidebar links to besides the content types: one of core's administration pages, or a menu item of a plugin's
// admin half. It shows only while its plugin, if it has one, is enabled in the site and the user holds its permission, if it
// names one. The label is a key of the admin's resources, or for a half the texts of its manifest's item, since the half's
// resources are not loaded before the half is.
internal sealed record AdminMenuEntry(
    string Route,
    string LabelKey,
    AdminMenuSection Section,
    string? Permission = null,
    string? Plugin = null,
    AdminManifestMenuItem? Item = null)
{
    public int Order => Item?.Order ?? 0;
}

// The sidebar links only to pages that exist: the menu items of the app's admin halves, and core's administration pages as
// the issues that add them add their entries.
internal sealed class AdminMenu
{
    public IReadOnlyList<AdminMenuEntry> Entries { get; init; } = [];
}
