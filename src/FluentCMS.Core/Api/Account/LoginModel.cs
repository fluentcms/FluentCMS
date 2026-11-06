using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Core.Api.Account;

public sealed class LoginModel : AccountPageModel
{
    // The same answer for every refusal, so that the page does not tell which names exist or are locked out.
    internal const string Refused = "Sign-in failed. Check the user name and password; after several failed attempts, wait a few minutes before you try again.";

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    // What the page that sent the user here reports: invitation-accepted, password-reset or external-failed.
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public IReadOnlyList<AuthenticationScheme> ExternalProviders { get; private set; } = [];

    // False with cms.Sso.DisableLocalLogin: the page offers the external providers only.
    public bool LocalLogin => !Service<SsoOptions>().DisableLocalLogin;

    public string? Notice => Status switch
    {
        "invitation-accepted" => "Your account is ready. Sign in with your new password.",
        "password-reset" => "Your password was changed. Sign in with the new one.",
        _ => null,
    };

    public string? Problem => Status == "external-failed" ? "The sign-in with the external provider did not complete. Try again." : null;

    public async Task OnGetAsync() => ExternalProviders = [.. await SignInManager.GetExternalAuthenticationSchemesAsync()];

    public async Task<IActionResult> OnPostAsync()
    {
        ExternalProviders = [.. await SignInManager.GetExternalAuthenticationSchemesAsync()];
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await SignInManager.PasswordSignInAsync(Input.UserName.Trim(), Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return LocalRedirect(LocalUrl(ReturnUrl));
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./TwoFactor", new { ReturnUrl = LocalUrl(ReturnUrl), Input.RememberMe });
        }

        ModelState.AddModelError(string.Empty, Refused);
        return Page();
    }

    // Starts a sign-in with an external provider, which comes back to the external login page.
    public async Task<IActionResult> OnPostExternalAsync(string provider)
    {
        ExternalProviders = [.. await SignInManager.GetExternalAuthenticationSchemesAsync()];
        if (!ExternalProviders.Any(scheme => scheme.Name == provider))
        {
            return BadRequest();
        }

        var callback = Url.Page("./ExternalLogin", pageHandler: null, values: new { ReturnUrl = LocalUrl(ReturnUrl) });
        return Challenge(SignInManager.ConfigureExternalAuthenticationProperties(provider, callback), provider);
    }
}

public sealed class LoginInput
{
    [Required]
    [Display(Name = "User name or email")]
    public string UserName { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }
}
