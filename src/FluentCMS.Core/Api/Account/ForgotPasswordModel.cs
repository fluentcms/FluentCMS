using System.ComponentModel.DataAnnotations;
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

        // The link is built from the request, as the issuer is; a host keeps a forged Host header out with AllowedHosts.
        var link = new Uri($"{Request.Scheme}://{Request.Host.ToUriComponent()}{Request.PathBase.ToUriComponent()}{HostedPagePaths.ResetPassword}");
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
