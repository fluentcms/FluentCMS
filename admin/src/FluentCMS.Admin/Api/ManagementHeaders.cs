namespace FluentCMS.Admin.Api;

internal static class ManagementHeaders
{
    public const string Site = "X-Cms-Site";
    public const string Locale = "X-Cms-Locale";
    public const string IfMatch = "If-Match";
    public const string IdempotencyKey = "Idempotency-Key";

    // Whether SessionHeadersHandler adds the shell's site and the view's locale; ManagementApi decides from the request.
    public static readonly HttpRequestOptionsKey<bool> AddSessionSite = new("FluentCMS.Admin.AddSessionSite");
    public static readonly HttpRequestOptionsKey<bool> AddSessionLocale = new("FluentCMS.Admin.AddSessionLocale");

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
