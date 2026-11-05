using FluentCMS.Content;

namespace FluentCMS.Core.Authorization;

// Which roles a principal holds: UserPrincipalRoles reads cms_user_role for admin users. The anonymous principal does
// not come through this: it always resolves to the Public role of the site.
internal interface IPrincipalRoles
{
    ValueTask<IReadOnlyCollection<Guid>> RolesOfAsync(Principal principal, CancellationToken cancellationToken = default);
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

    public static IReadOnlyCollection<Guid> FromClaim(Principal principal) =>
        (principal.Claim(Claim) ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(text => Guid.TryParse(text, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
}
