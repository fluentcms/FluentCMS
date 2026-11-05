using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity;

// Every way in goes through CanSignInAsync, so a deactivated user is refused whatever its password, lockout or provider.
internal sealed class CmsSignInManager(
    UserManager<CmsUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<CmsUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<CmsUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<CmsUser> confirmation)
    : SignInManager<CmsUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(CmsUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.DeactivatedAt is null && await base.CanSignInAsync(user);
    }
}
