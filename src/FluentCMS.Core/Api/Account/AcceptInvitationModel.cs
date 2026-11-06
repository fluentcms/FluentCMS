using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Core.Api.Account;

// The page the invitation email links to: the invited user chooses a user name and a password, and then signs in.
public sealed class AcceptInvitationModel : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public AcceptInvitationInput Input { get; set; } = new();

    // The invited address, or null when the token is not one that would be accepted now.
    public string? Email { get; private set; }

    public async Task OnGetAsync() => Email = await InvitedAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        Email = await InvitedAsync();
        if (Email is null || !ModelState.IsValid)
        {
            return Page();
        }

        var result = await Service<InvitationService>().AcceptAsync(Token!, Input.UserName.Trim(), Input.Password, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            return RedirectToPage("./Login", new { Status = "invitation-accepted" });
        }

        AddErrors(result);
        return Page();
    }

    private async Task<string?> InvitedAsync() =>
        string.IsNullOrEmpty(Token) ? null : (await Service<InvitationService>().FindAsync(Token, HttpContext.RequestAborted))?.Email;
}

public sealed class AcceptInvitationInput
{
    [Required]
    [StringLength(256)]
    [Display(Name = "User name")]
    public string UserName { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Repeat the password")]
    public string ConfirmPassword { get; set; } = "";
}
