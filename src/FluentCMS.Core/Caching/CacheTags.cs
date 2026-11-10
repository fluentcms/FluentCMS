using FluentCMS.Schema;

namespace FluentCMS.Core.Caching;

// The output cache tags of spec 4.10. The entries of a global type belong to no site, so its tag names none and covers
// what every site serves of it. The tag header lists them apart by a space for Surrogate-Key, as Fastly reads it, and by a
// comma for any other name, as Cloudflare's Cache-Tag and Akamai's Edge-Cache-Tag are read.
internal static class CacheTags
{
    public const string GlobalSite = "global";

    private const string SurrogateKey = "Surrogate-Key";

    // What a Delivery response writes itself, what the server or a proxy owns, or what would keep it out of the output cache.
    private static readonly HashSet<string> Written = new(StringComparer.OrdinalIgnoreCase)
    {
        "Age", "Cache-Control", "Connection", "Content-Encoding", "Content-Length", "Content-Type", "Date", "ETag", "Expires", "Keep-Alive",
        "Last-Modified", "Server", "Set-Cookie", "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Vary",
    };

    public static string Site(Guid site) => $"site:{site:D}";

    public static string Type(SchemaType type, Guid site) => Type(type.Name, type.Scope == ContentScope.Global, site);

    public static string Type(string type, bool global, Guid site) => global ? $"type:{GlobalSite}:{type}" : $"type:{site:D}:{type}";

    public static string Entry(Guid entry) => $"entry:{entry:D}";

    public static string HeaderValue(string header, IEnumerable<string> tags) =>
        string.Join(string.Equals(header, SurrogateKey, StringComparison.OrdinalIgnoreCase) ? ' ' : ',', tags);

    // A token of RFC 9110 (5.6.2).
    public static bool IsHeaderName(string name) =>
        name.Length > 0 && !Written.Contains(name) && name.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c));
}
