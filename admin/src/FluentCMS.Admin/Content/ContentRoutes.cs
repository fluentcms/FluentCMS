using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Content;

// The admin's routes of content (spec 3.2).
internal static class ContentRoutes
{
    // Where the Management API's paths start, as a create's Location writes them.
    public const string ManagementPrefix = "api/management/v1/";

    public static string Type(string type) => $"content/{Uri.EscapeDataString(type)}";

    public static string New(string type) => $"{Type(type)}/new";

    public static string Entry(string type, Guid id) => $"{Type(type)}/{id:D}";

    // A route that names a content locale in its query string (spec 4.17); without one it keeps the locale there is.
    public static string InLocale(string route, string? locale) =>
        string.IsNullOrEmpty(locale) ? route : $"{route}{(route.Contains('?', StringComparison.Ordinal) ? '&' : '?')}locale={Uri.EscapeDataString(locale)}";

    // The content locale an address names, or null.
    public static string? LocaleOf(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var pair = address.Query.TrimStart('?').Split('&').LastOrDefault(item => item.StartsWith("locale=", StringComparison.Ordinal));
        return pair is null ? null : Uri.UnescapeDataString(pair["locale=".Length..].Replace('+', ' '));
    }

    // Whether the address is the list of the type, or the form of its entry (the new entry's without an id). While the router
    // waits in OnNavigateAsync for the next page, the page drawn before gets that address's query, which it must not follow.
    public static bool IsList(NavigationManager navigation, string type) =>
        SegmentsOf(navigation) is [var content, var named] && IsContent(content, named, type);

    public static bool IsEntry(NavigationManager navigation, string type, Guid? id) =>
        SegmentsOf(navigation) is [var content, var named, var last] && IsContent(content, named, type)
            && (id is { } entry ? Guid.TryParse(last, out var parsed) && parsed == entry : string.Equals(last, "new", StringComparison.OrdinalIgnoreCase));

    private static bool IsContent(string content, string named, string type) =>
        string.Equals(content, "content", StringComparison.OrdinalIgnoreCase) && string.Equals(named, type, StringComparison.OrdinalIgnoreCase);

    private static string[] SegmentsOf(NavigationManager navigation)
    {
        var route = navigation.ToBaseRelativePath(navigation.Uri);
        var end = route.IndexOfAny(['?', '#']);
        return [.. (end < 0 ? route : route[..end]).Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString)];
    }
}
