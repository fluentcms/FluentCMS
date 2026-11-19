namespace FluentCMS.Plugins.Media;

// The "accept" setting of a media field: MIME types such as image/png and families such as image/*, matched without regard
// to case and to the parameters after ';'. Linked by the analyzers and the CLI, so it builds for netstandard2.0 too.
internal static class MediaAccept
{
    // RFC 6838 allows a little more punctuation, which no registered type uses and which generated comments and URLs would
    // have to escape.
    private const int MaxNameLength = 127;

    public static bool IsPattern(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var slash = value.IndexOf('/');
        return slash > 0
            && IsName(value, 0, slash)
            && (value.Length == slash + 2 && value[slash + 1] == '*' || IsName(value, slash + 1, value.Length));
    }

    // What schema validation says of a setting that is a list of strings, or null when every item is a pattern.
    public static string? Problem(object value) => value is IEnumerable<string?> items && First(items) is { } wrong
        ? $"has '{wrong}', which is not a MIME type such as image/png or a family such as image/*"
        : null;

    public static bool Matches(IReadOnlyCollection<string> accept, string? mimeType)
    {
        if (accept.Count == 0)
        {
            return true;
        }

        if (mimeType is null)
        {
            return false;
        }

        var end = mimeType.IndexOf(';');
        var type = (end < 0 ? mimeType : mimeType.Substring(0, end)).Trim();
        foreach (var item in accept)
        {
            if (item.EndsWith("/*", StringComparison.Ordinal)
                ? type.Length > item.Length - 1 && type.StartsWith(item.Substring(0, item.Length - 1), StringComparison.OrdinalIgnoreCase)
                : string.Equals(item, type, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? First(IEnumerable<string?> items)
    {
        foreach (var item in items)
        {
            if (!IsPattern(item))
            {
                return item ?? "null";
            }
        }

        return null;
    }

    private static bool IsName(string text, int start, int end)
    {
        if (end - start is < 1 or > MaxNameLength || !IsLetterOrDigit(text[start]))
        {
            return false;
        }

        for (var i = start + 1; i < end; i++)
        {
            if (!IsLetterOrDigit(text[i]) && text[i] is not ('.' or '+' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLetterOrDigit(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
}
