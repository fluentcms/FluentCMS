using FluentCMS.Content;

namespace FluentCMS.Core.Authorization;

// Which roles a principal holds: UserPrincipalRoles reads cms_user_role for admin users. The anonymous principal does
// not come through this: it always resolves to the Public role of the site.
internal interface IPrincipalRoles
{
    ValueTask<IReadOnlyCollection<Guid>> RolesOfAsync(Principal principal, CancellationToken cancellationToken = default);

    // The content types the principal's own credential binds it to as it is now, or null when it binds it to none. A preview
    // token carries the binding of its entry instead, so this is what its targets are held to (spec 4.7).
    ValueTask<IReadOnlySet<string>?> TypesOfAsync(Principal principal, CancellationToken cancellationToken = default);
}

// Reads the role ids from a claim, as spec 4.7 has the API key handler do. An id that does not parse grants nothing.
internal sealed class ClaimPrincipalRoles : IPrincipalRoles
{
    public const string Claim = "roles";

    public ValueTask<IReadOnlyCollection<Guid>> RolesOfAsync(Principal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return ValueTask.FromResult(FromClaim(principal));
    }

    // Only the claim is known here, so a preview principal stays held to the type of its entry.
    public ValueTask<IReadOnlySet<string>?> TypesOfAsync(Principal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return ValueTask.FromResult(PrincipalBinding.Of(principal).Types);
    }

    public static IReadOnlyCollection<Guid> FromClaim(Principal principal) =>
        (principal.Claim(Claim) ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(text => Guid.TryParse(text, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
}
