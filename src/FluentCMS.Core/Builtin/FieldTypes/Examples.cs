namespace FluentCMS.Core.Builtin.FieldTypes;

internal static class Examples
{
    public static string Fit(string text, int? maxLength)
    {
        return maxLength is { } limit && text.Length > limit ? text[..limit] : text;
    }

    public static string AtMost(int? maxLength)
    {
        return maxLength is { } limit ? $" of at most {limit} characters" : string.Empty;
    }
}
