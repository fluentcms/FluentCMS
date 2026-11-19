using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Api.Account;

// A signed-in user sets up an authenticator app. The authorize endpoint sends a user here whose role requires two-factor and
// who has none. A code of the new key turns two-factor on, shows the recovery codes once, and makes the sign-in one with the
// second factor (amr=mfa), since the user has just given both.
public sealed class EnableTwoFactorModel : AccountPageModel
{
    [BindProperty]
    public EnableTwoFactorInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string SharedKey { get; private set; } = "";

    public string AuthenticatorUri { get; private set; } = "";

    public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];

    public string ContinueUrl => LocalUrl(ReturnUrl);

    public async Task<IActionResult> OnGetAsync()
    {
        if (await SignedInUserAsync() is not { } user)
        {
            return RedirectToLogin(Request.PathBase + Request.Path + Request.QueryString);
        }

        if (await Service<TwoFactorService>().IsOnAsync(user))
        {
            return LocalRedirect(ContinueUrl);
        }

        await LoadSetupAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await SignedInUserAsync() is not { } user)
        {
            return RedirectToLogin(Request.PathBase + Request.Path + Request.QueryString);
        }

        if (await Service<TwoFactorService>().IsOnAsync(user))
        {
            return LocalRedirect(ContinueUrl);
        }

        if (ModelState.IsValid)
        {
            try
            {
                RecoveryCodes = await Service<TwoFactorService>().VerifyAsync(user, Input.Code);
            }
            catch (FluentCMS.Content.ValidationException exception)
            {
                foreach (var error in exception.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Message);
                }
            }
        }

        if (RecoveryCodes.Count == 0)
        {
            await LoadSetupAsync(user);
            return Page();
        }

        // The sign-in keeps how it was made, pwd or the provider, beside the mfa it now has.
        var cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        List<Claim> methods = [.. (cookie.Principal?.FindAll(claim => claim.Type is Claims.AuthenticationMethodReference or ClaimTypes.AuthenticationMethod) ?? [])
            .Where(claim => claim.Value != TokenClaims.MultiFactor)
            .Select(claim => new Claim(claim.Type, claim.Value))];
        methods.Add(new Claim(Claims.AuthenticationMethodReference, TokenClaims.MultiFactor));
        await SignInManager.SignInWithClaimsAsync(user, cookie.Properties, methods);
        return Page();
    }

    private async Task LoadSetupAsync(CmsUser user)
    {
        var issuer = (await Service<ISettings<BrandingSettings>>().GetDefaultAsync(HttpContext.RequestAborted)).Name;
        var setup = await Service<TwoFactorService>().PendingAsync(user, issuer);
        SharedKey = TwoFactorService.FormatKey(setup.SharedKey);
        AuthenticatorUri = setup.AuthenticatorUri;
    }
}

public sealed class EnableTwoFactorInput
{
    [Required]
    [StringLength(7, MinimumLength = 6)]
    [Display(Name = "Code from the app")]
    public string Code { get; set; } = "";
}
