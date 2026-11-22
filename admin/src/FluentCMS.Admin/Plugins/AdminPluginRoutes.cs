namespace FluentCMS.Admin.Plugins;

// Matches a route relative to the app's base with the route templates of a half's pages, as the router reads them: a
// parameter takes one segment whatever its constraint, an optional one may be absent, and a catch-all takes the rest.
internal static class AdminPluginRoutes
{
    // The page whose template matches, the one with the most literal segments first, as the router prefers it.
    public static AdminManifestPage? Match(IEnumerable<AdminManifestPage> pages, string route)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(route);
        var segments = SegmentsOf(route);
        return pages
            .Select(page => (Page: page, Literals: Literals(page.Route, segments)))
            .Where(match => match.Literals >= 0)
            .OrderByDescending(match => match.Literals)
            .Select(match => match.Page)
            .FirstOrDefault();
    }

    public static bool Matches(string template, string route)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(route);
        return Literals(template, SegmentsOf(route)) >= 0;
    }

    private static string[] SegmentsOf(string route)
    {
        var end = route.IndexOfAny(['?', '#']);
        return [.. (end < 0 ? route : route[..end]).Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString)];
    }

    // The number of literal segments of a template that matches, or -1.
    private static int Literals(string template, string[] segments)
    {
        var parts = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var literals = 0;
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.StartsWith("{*", StringComparison.Ordinal))
            {
                return literals;
            }

            if (part.StartsWith('{'))
            {
                if (index >= segments.Length && !part.EndsWith("?}", StringComparison.Ordinal))
                {
                    return -1;
                }

                continue;
            }

            if (index >= segments.Length || !string.Equals(part, segments[index], StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }

            literals++;
        }

        return segments.Length <= parts.Length ? literals : -1;
    }
}
