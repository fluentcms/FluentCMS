using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Core.Api.Account;

// A signed-in user changes the password. The authorize endpoint sends a user who must change it here first.
public sealed class ChangePasswordModel : AccountPageModel
{
    [BindProperty]
    public ChangePasswordInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool Required { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await SignedInUserAsync() is not { } user)
        {
            return RedirectToLogin(Request.PathBase + Request.Path + Request.QueryString);
        }

        Required = user.MustChangePassword;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await SignedInUserAsync() is not { } user)
        {
            return RedirectToLogin(Request.PathBase + Request.Path + Request.QueryString);
        }

        Required = user.MustChangePassword;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await SignInManager.UserManager.ChangePasswordAsync(user, Input.CurrentPassword, Input.Password);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return Page();
        }

        // The change gave the user a new security stamp, which the cookie must carry to stay good.
        await SignInManager.RefreshSignInAsync(user);
        return LocalRedirect(LocalUrl(ReturnUrl));
    }
}

public sealed class ChangePasswordInput
{
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string Password { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Repeat the new password")]
    public string ConfirmPassword { get; set; } = "";
}
