namespace FluentCMS.Core.ProseMirror;

// Which URLs rich text may hold and render: one with a scheme of the list, in any case, or one with no scheme at all.
internal sealed class SafeUrl
{
    public static readonly string[] DefaultSchemes = ["http", "https", "mailto", "tel"];

    // Schemes that run script, embed content or reach the local machine; no configuration allows them.
    public static readonly string[] ForbiddenSchemes = ["javascript", "data", "vbscript", "blob", "file", "about"];

    // The attributes of a node or mark, or keys in the data of an inline block of no known type, that hold a URL.
    public static readonly string[] UrlAttributes = ["href", "src", "url", "cite", "action", "formaction", "poster", "background", "xlink:href"];

    private readonly HashSet<string> _schemes;

    public SafeUrl(IEnumerable<string> schemes) => _schemes = new HashSet<string>(schemes, StringComparer.OrdinalIgnoreCase);

    public static SafeUrl Default { get; } = new(DefaultSchemes);

    public static bool IsUrlAttribute(string name) => UrlAttributes.Contains(name, StringComparer.OrdinalIgnoreCase);

    public string? OrNull(string? url) => url is not null && IsSafe(url) ? url : null;

    public bool IsSafe(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Any(char.IsControl))
        {
            return false;
        }

        var end = SchemeEnd(trimmed);
        var prefix = end < 0 ? trimmed : trimmed[..end];

        // A character reference such as &#58; can spell the colon once a consumer decodes the URL.
        if (HasCharacterReference(prefix))
        {
            return false;
        }

        return end < 0 || trimmed[end] != ':' || _schemes.Contains(prefix);
    }

    private static int SchemeEnd(string url)
    {
        for (var index = 0; index < url.Length; index++)
        {
            if (url[index] is '/' or '?' or ':' || (url[index] == '#' && (index == 0 || url[index - 1] != '&')))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasCharacterReference(string text)
    {
        for (var index = text.IndexOf('&', StringComparison.Ordinal); index >= 0; index = text.IndexOf('&', index + 1))
        {
            if (index + 1 < text.Length && (text[index + 1] == '#' || char.IsAsciiLetter(text[index + 1])))
            {
                return true;
            }
        }

        return false;
    }
}
