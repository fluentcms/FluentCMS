using System.Security.Claims;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;

namespace FluentCMS.Core.Api.Common;

// The request's ClaimsPrincipal as the engine's Principal. Every claim but the three that make the principal is kept,
// so that a row rule can read one; a claim the handler wrote more than once becomes one space-separated value.
internal static class RequestPrincipal
{
    public static Principal From(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Identity?.IsAuthenticated != true)
        {
            return Principal.Anonymous;
        }

        // A handler that writes no usable kind or subject is a fault of the host, not of the client.
        var kindText = user.FindFirst(CmsClaimTypes.Kind)?.Value;
        var kind = kindText is not null && Enum.GetNames<PrincipalKind>().Contains(kindText, StringComparer.Ordinal)
            ? Enum.Parse<PrincipalKind>(kindText)
            : PrincipalKind.Anonymous;
        if (kind is PrincipalKind.System or PrincipalKind.Anonymous)
        {
            throw new InvalidOperationException($"The authenticated request has no usable {CmsClaimTypes.Kind} claim.");
        }

        if (!Guid.TryParse(user.FindFirst(CmsClaimTypes.Subject)?.Value, out var id))
        {
            throw new InvalidOperationException($"The authenticated request has no usable {CmsClaimTypes.Subject} claim.");
        }

        var claims = user.Claims
            .Where(claim => claim.Type is not (CmsClaimTypes.Subject or CmsClaimTypes.Name or CmsClaimTypes.Kind))
            .GroupBy(claim => claim.Type, StringComparer.Ordinal)
            .Select(group => KeyValuePair.Create(group.Key, string.Join(' ', group.Select(claim => claim.Value))));

        return new Principal(kind, id, user.FindFirst(CmsClaimTypes.Name)?.Value ?? "", claims);
    }

    // The sites a key is bound to, or null when it is bound to none. A binding that names no site reaches none.
    public static IReadOnlyList<Guid>? BoundSites(Principal principal) =>
        principal.Claim(CmsClaimTypes.Sites) is { } text
            ? [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(item => Guid.TryParse(item, out var site) ? site : Guid.Empty).Where(site => site != Guid.Empty).Distinct()]
            : null;
}
