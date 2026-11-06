using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Account;

// The base of the models of the hosted pages. They are public so that a host's own page at the same path can use them;
// the services they call are reached through the request, since those are internal.
public abstract class AccountPageModel : PageModel
{
    private protected SignInManager<CmsUser> SignInManager => Service<SignInManager<CmsUser>>();

    private protected T Service<T>()
        where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();

    // Only a path of this host, so that a link cannot send the user elsewhere after signing in.
    private protected string LocalUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");

    // The user of the sign-in cookie, if it is still good: the security stamp holds and the user may sign in.
    private protected async Task<CmsUser?> SignedInUserAsync()
    {
        var cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = cookie.Succeeded ? await SignInManager.ValidateSecurityStampAsync(cookie.Principal) : null;
        return user is not null && await SignInManager.CanSignInAsync(user) ? user : null;
    }

    private protected IActionResult RedirectToLogin(string? returnUrl) =>
        Redirect(Request.PathBase + HostedPagePaths.Login + QueryString.Create("ReturnUrl", LocalUrl(returnUrl)));

    private protected void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}
