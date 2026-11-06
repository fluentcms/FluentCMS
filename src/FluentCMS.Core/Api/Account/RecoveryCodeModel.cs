using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Core.Api.Account;

// The second step with a recovery code in place of the authenticator's; each code works once. Generating them is #2780.
public sealed class RecoveryCodeModel : AccountPageModel
{
    [BindProperty]
    public RecoveryCodeInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync() =>
        await SignInManager.GetTwoFactorAuthenticationUserAsync() is null ? RedirectToLogin(ReturnUrl) : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (await SignInManager.GetTwoFactorAuthenticationUserAsync() is null)
        {
            return RedirectToLogin(ReturnUrl);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await SignInManager.TwoFactorRecoveryCodeSignInAsync(Input.Code.Replace(" ", "", StringComparison.Ordinal));
        if (result.Succeeded)
        {
            return LocalRedirect(LocalUrl(ReturnUrl));
        }

        ModelState.AddModelError(string.Empty, "The recovery code is not right, or the account cannot sign in now.");
        return Page();
    }
}

public sealed class RecoveryCodeInput
{
    [Required]
    [StringLength(64)]
    [Display(Name = "Recovery code")]
    public string Code { get; set; } = "";
}
