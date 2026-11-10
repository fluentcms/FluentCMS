using FluentCMS.Content;

namespace FluentCMS.Core.Authorization;

// The sites and content types a principal is bound to, from its fcms_sites and fcms_types claims; null is no binding. A
// claim that is there but names nothing usable binds to nothing, so a damaged claim reaches nothing. A site binding needs
// the operation to be in one of the sites, so an operation on every site is out of it. A type binding leaves only the
// content permissions of those types: what is not about content belongs to no type. A preview token is also bound to one
// entry, which EffectivePermissions adds to the row rule. Its locale is not a binding here: the values a locale falls back
// to are read in other locales, and Delivery refuses a request for another one.
internal sealed record PrincipalBinding(IReadOnlySet<Guid>? Sites, IReadOnlySet<string>? Types, Guid? Entry = null)
{
    private const string ContentPrefix = FluentCMS.Authorization.Permission.ContentPrefix + ".";

    public static PrincipalBinding Of(Principal principal) =>
        new(
            Split(principal.Claim(CmsClaimTypes.Sites))?.Select(text => Guid.TryParse(text, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToHashSet(),
            Split(principal.Claim(CmsClaimTypes.Types))?.ToHashSet(StringComparer.Ordinal),
            principal.Claim(CmsClaimTypes.Entry) is { } entry ? (Guid.TryParse(entry, out var id) ? id : Guid.Empty) : null);

    public bool Allows(string permission, AccessTarget target) =>
        (Sites is null || (target.Site is { } site && Sites.Contains(site.Value)))
        && (Types is null || (TypeOf(permission) is { } type && Types.Contains(type)));

    private static string? TypeOf(string permission)
    {
        if (!permission.StartsWith(ContentPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = permission.LastIndexOf('.');
        return end > ContentPrefix.Length ? permission[ContentPrefix.Length..end] : null;
    }

    private static string[]? Split(string? claim) => claim?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
