using System.Globalization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Shell;

internal enum ShellSectionKind
{
    Collections,
    Singletons,
    Content,
    Media,
    Administration,
    Settings,
}

// A link of the sidebar: to a content type's list, or to a page of the menu.
internal sealed record ShellLink(string Href, SchemaTypeDescription? Type = null, AdminMenuEntry? Entry = null)
{
    public bool Global => string.Equals(Type?.Scope, "global", StringComparison.Ordinal);
}

internal sealed record ShellSection(ShellSectionKind Kind, IReadOnlyList<ShellLink> Links);

// The sections of the sidebar for the site of the context (spec 4.1): the collections and the singletons the user may read,
// in schema order, then the pages of the menu it may use. A section without links is left out.
internal static class ShellNavigation
{
    public static string ContentRoute(string type) => $"content/{Uri.EscapeDataString(type)}";

    public static IReadOnlyList<ShellSection> Build(AdminContext context, AdminMenu menu)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(menu);

        var types = (context.Schema?.Types ?? []).Where(type => context.MayRead(type.Name)).ToList();
        List<ShellSection> sections =
        [
            new(ShellSectionKind.Collections, [.. types.Where(type => type.Kind == "collection").Select(Link)]),
            new(ShellSectionKind.Singletons, [.. types.Where(type => type.Kind == "singleton").Select(Link)]),
        ];

        var entries = menu.Entries
            .Where(entry => (entry.Plugin is null || context.PluginEnabled(entry.Plugin)) && (entry.Permission is null || context.Allows(entry.Permission)))
            .OrderBy(entry => entry.Order)
            .ThenBy(entry => entry.Item?.LabelFor(CultureInfo.CurrentUICulture), StringComparer.CurrentCulture)
            .ToList();
        foreach (var (section, kind) in new[]
        {
            (AdminMenuSection.Content, ShellSectionKind.Content),
            (AdminMenuSection.Media, ShellSectionKind.Media),
            (AdminMenuSection.Administration, ShellSectionKind.Administration),
            (AdminMenuSection.Settings, ShellSectionKind.Settings),
        })
        {
            sections.Add(new(kind, [.. entries.Where(entry => entry.Section == section).Select(entry => new ShellLink(entry.Route, Entry: entry))]));
        }

        return [.. sections.Where(section => section.Links.Count > 0)];
    }

    private static ShellLink Link(SchemaTypeDescription type) => new(ContentRoute(type.Name), type);
}
