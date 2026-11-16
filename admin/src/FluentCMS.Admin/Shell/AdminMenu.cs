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
// admin half (A-19). It shows only while its plugin, if it has one, is enabled in the site and the user holds its permission,
// if it names one. The label is a key of the admin's resources.
internal sealed record AdminMenuEntry(string Route, string LabelKey, AdminMenuSection Section, string? Permission = null, string? Plugin = null);

// The sidebar links only to pages that exist, and no page of core's administration or of a plugin's admin half exists in A1,
// so the list starts empty; the issues that add the pages add their entries.
internal sealed class AdminMenu
{
    public IReadOnlyList<AdminMenuEntry> Entries { get; init; } = [];
}
