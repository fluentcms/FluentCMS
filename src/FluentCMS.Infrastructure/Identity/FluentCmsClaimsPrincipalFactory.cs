using System.Security.Claims;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Identity;

// Adds the display name, so the initiator a signed-in user is recorded as carries their name rather than their email.
internal sealed class FluentCmsClaimsPrincipalFactory(
    UserManager<FluentCmsUser> users,
    RoleManager<FluentCmsRole> roles,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<FluentCmsUser, FluentCmsRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(FluentCmsUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(FluentCmsClaimTypes.DisplayName, user.DisplayName));
        return identity;
    }
}
