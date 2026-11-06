using FluentCMS.Schema;

namespace FluentCMS.Core.Caching;

// The output cache tags of spec 4.10. The entries of a global type belong to no site, so its tag names none and covers
// what every site serves of it.
internal static class CacheTags
{
    public const string GlobalSite = "global";

    public static string Site(Guid site) => $"site:{site:D}";

    public static string Type(SchemaType type, Guid site) => Type(type.Name, type.Scope == ContentScope.Global, site);

    public static string Type(string type, bool global, Guid site) => global ? $"type:{GlobalSite}:{type}" : $"type:{site:D}:{type}";

    public static string Entry(Guid entry) => $"entry:{entry:D}";
}
