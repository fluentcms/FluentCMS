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
}
