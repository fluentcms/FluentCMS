using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Core.Api.Account;

// Sends a link to reset the password. The answer is the same whether or not the address belongs to a user.
public sealed class ForgotPasswordModel : AccountPageModel
{
    [BindProperty]
    public ForgotPasswordInput Input { get; set; } = new();

    public bool Sent { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var link = Service<PublicOrigin>().Link(Request, HostedPagePaths.ResetPassword);
        await Service<PasswordResetService>().RequestAsync(Input.Email, link, HttpContext.RequestAborted);
        Sent = true;
        return Page();
    }
}

public sealed class ForgotPasswordInput
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";
}
