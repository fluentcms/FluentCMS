using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Core.Api.Account;

// The second step of a sign-in for a user with an authenticator app, after the password. A user whose role requires
// two-factor is not offered to skip it on this device, since the authorize endpoint would not take a sign-in that did.
public sealed class TwoFactorModel : AccountPageModel
{
    [BindProperty]
    public TwoFactorInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    public bool CanRememberDevice { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await SignInManager.GetTwoFactorAuthenticationUserAsync() is not { } user)
        {
            return RedirectToLogin(ReturnUrl);
        }

        CanRememberDevice = !await Service<TwoFactorService>().IsRequiredAsync(user.Id, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await SignInManager.GetTwoFactorAuthenticationUserAsync() is not { } user)
        {
            return RedirectToLogin(ReturnUrl);
        }

        CanRememberDevice = !await Service<TwoFactorService>().IsRequiredAsync(user.Id, HttpContext.RequestAborted);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var code = Input.Code.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        var result = await SignInManager.TwoFactorAuthenticatorSignInAsync(code, RememberMe, Input.RememberMachine && CanRememberDevice);
        if (result.Succeeded)
        {
            return LocalRedirect(LocalUrl(ReturnUrl));
        }

        ModelState.AddModelError(string.Empty, "The code is not right, or the account cannot sign in now.");
        return Page();
    }
}

public sealed class TwoFactorInput
{
    [Required]
    [StringLength(7, MinimumLength = 6)]
    [Display(Name = "Authenticator code")]
    public string Code { get; set; } = "";

    [Display(Name = "Do not ask again on this device")]
    public bool RememberMachine { get; set; }
}
