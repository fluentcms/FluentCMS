namespace FluentCMS.Admin.Api;

internal static class ManagementHeaders
{
    public const string Site = "X-Cms-Site";
    public const string Locale = "X-Cms-Locale";
    public const string IfMatch = "If-Match";
    public const string IdempotencyKey = "Idempotency-Key";

    // A weak tag a proxy passed on is sent by its value, as the server reads it; a bare version gets its quotes.
    public static string StrongTag(string tag)
    {
        var value = tag.Trim();
        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        return value.StartsWith('"') ? value : $"\"{value}\"";
    }
}
