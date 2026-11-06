using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Sso;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Core.Api.Account;

// The page the invitation email links to: the invited user chooses a user name and a password, and then signs in, or
// accepts through a single sign-on provider, which links the provider's account and signs in. With local login disabled
// only the providers are offered.
public sealed class AcceptInvitationModel : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public AcceptInvitationInput Input { get; set; } = new();

    // The invited address, or null when the token is not one that would be accepted now.
    public string? Email { get; private set; }

    public IReadOnlyList<AuthenticationScheme> ExternalProviders { get; private set; } = [];

    public bool LocalLogin => !Service<SsoOptions>().DisableLocalLogin;

    public string? Problem { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
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

    // Starts the sign-in with a provider, which comes back to OnGetExternalAsync with the token.
    public async Task<IActionResult> OnPostExternalAsync(string provider)
    {
        await LoadAsync();
        if (Email is null)
        {
            return Page();
        }

        if (!ExternalProviders.Any(scheme => scheme.Name == provider))
        {
            return BadRequest();
        }

        var callback = Url.Page("./AcceptInvitation", pageHandler: "External", values: new { Token });
        return Challenge(SignInManager.ConfigureExternalAuthenticationProperties(provider, callback), provider);
    }

    public async Task<IActionResult> OnGetExternalAsync()
    {
        await LoadAsync();
        var info = await SignInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            Problem = Email is null ? null : "The sign-in with the provider did not complete. Try again.";
            return Page();
        }

        // The external cookie of a sign-in that accepted nothing must not be taken for the next one.
        if (Email is null)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Page();
        }

        var (accepted, signedIn) = await Service<SsoSignIn>().AcceptInvitationAsync(info, Token!, HttpContext.RequestAborted);
        if (!accepted.Succeeded)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            AddErrors(accepted);
            return Page();
        }

        if (signedIn.Succeeded)
        {
            return LocalRedirect(LocalUrl(null));
        }

        return signedIn.RequiresTwoFactor
            ? RedirectToPage("./TwoFactor", new { ReturnUrl = LocalUrl(null) })
            : RedirectToPage("./Login", new { Status = "invitation-linked" });
    }

    private async Task LoadAsync()
    {
        Email = await InvitedAsync();
        var sso = Service<SsoOptions>();
        ExternalProviders = [.. (await SignInManager.GetExternalAuthenticationSchemesAsync()).Where(scheme => sso.Providers.ContainsKey(scheme.Name))];
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
