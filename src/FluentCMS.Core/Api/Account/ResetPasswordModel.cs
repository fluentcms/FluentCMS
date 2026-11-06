using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Core.Api.Account;

// The page the reset email links to, with the user and the token in the query.
public sealed class ResetPasswordModel : AccountPageModel
{
    [BindProperty(SupportsGet = true, Name = "user")]
    public Guid UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public NewPasswordInput Input { get; set; } = new();

    public bool HasLink => UserId != Guid.Empty && !string.IsNullOrEmpty(Token);

    public async Task<IActionResult> OnPostAsync()
    {
        if (!HasLink || !ModelState.IsValid)
        {
            return Page();
        }

        var result = await Service<PasswordResetService>().ResetAsync(UserId, Token!, Input.Password);
        if (result.Succeeded)
        {
            return RedirectToPage("./Login", new { Status = "password-reset" });
        }

        AddErrors(result);
        return Page();
    }
}

public sealed class NewPasswordInput
{
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
