using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Core.Api.Account;

// Where an external provider returns to. A user linked to the external login signs in; for any other the page says so,
// and admin SSO (#2781) provisions one here.
public sealed class ExternalLoginModel : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ProviderName { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var info = await SignInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return RedirectToPage("./Login", new { ReturnUrl = LocalUrl(ReturnUrl), Status = "external-failed" });
        }

        var result = await SignInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false);
        if (result.Succeeded)
        {
            return LocalRedirect(LocalUrl(ReturnUrl));
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./TwoFactor", new { ReturnUrl = LocalUrl(ReturnUrl) });
        }

        ProviderName = info.ProviderDisplayName ?? info.LoginProvider;
        return Page();
    }
}
