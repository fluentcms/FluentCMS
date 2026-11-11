using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Members;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using ValidationException = FluentCMS.Content.ValidationException;

namespace FluentCMS.Core.Api.Account;

// The base of the models of the member pages at /account/members. They are public so that a host's own page at the same
// path can use them; the services they call are reached through the request, since those are internal. The pages sign
// in with the member cookie and never read the admin's.
public abstract class MemberPageModel : PageModel
{
    private protected T Service<T>()
        where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();

    private protected MemberAccountService Account => Service<MemberAccountService>();

    // Only a path of this host, so that a link cannot send the member elsewhere after signing in.
    private protected string LocalUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Content("~/");

    private protected void AddErrors(ValidationException exception)
    {
        foreach (var error in exception.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Message);
        }
    }

    // The providers of cms.Members.Providers, which are the only ones a member page offers: a provider of cms.Sso
    // signs in admin users, and has no button here.
    private protected IReadOnlyList<MemberProviderButton> ProviderButtons() =>
        [.. Service<MemberOptions>().Providers.Select(entry => new MemberProviderButton(entry.Key, entry.Value.Name(entry.Key)))];

    // Sends the browser to the provider, which comes back to redirectUri. Only a provider of the member pool is
    // challenged, whatever the form names.
    private protected IActionResult ChallengeProvider(string? provider, string redirectUri, Guid? linkFor = null)
    {
        var known = Service<MemberOptions>().Providers.Keys.FirstOrDefault(name => string.Equals(name, provider, StringComparison.OrdinalIgnoreCase));
        return known is null
            ? BadRequest()
            : Challenge(MemberProviders.Challenge(redirectUri, linkFor), MemberProvider.SchemeOf(known));
    }
}

// A button of a member page that starts a sign-in at a provider: the provider's name and the text it is shown with.
public sealed record MemberProviderButton(string Name, string DisplayName);

public sealed class MemberLoginModel : MemberPageModel
{
    // The same answer for every refusal, so that the page does not tell which addresses are members, which are not
    // verified yet and which are locked out.
    internal const string Refused = "Sign-in failed. Check the email address and password. A new account signs in once its address is verified, and after several failed attempts you wait a few minutes before you try again.";

    [BindProperty]
    public MemberLoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    // What the page that sent the member here reports: email-verified, password-reset or external-failed.
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public bool RegistrationOpen => Service<MemberOptions>().AllowRegistration;

    public IReadOnlyList<MemberProviderButton> ExternalProviders => ProviderButtons();

    public string? Problem => Status == "external-failed" ? "The sign-in with the external provider did not complete. Try again." : null;

    public string? Notice => Status switch
    {
        "email-verified" => "Your email address is verified. Sign in with your password.",
        "password-reset" => "Your password was changed. Sign in with the new one.",
        _ => null,
    };

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await Service<MemberSignInManager>().PasswordSignInAsync(Input.Email.Trim(), Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return LocalRedirect(LocalUrl(ReturnUrl));
        }

        ModelState.AddModelError(string.Empty, Refused);
        return Page();
    }

    // Starts a sign-in with a provider of the member pool, which comes back to the member external login page.
    public IActionResult OnPostExternal(string? provider) =>
        ChallengeProvider(provider, Url.Page("./ExternalLogin", pageHandler: null, values: new { ReturnUrl = LocalUrl(ReturnUrl) })!);
}

public sealed class MemberLoginInput
{
    [Required]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }
}

// Asks before signing out. With the request of a client, which /connect/logout sent here, the form posts it back there
// with an antiforgery token and the mark of this page, so that the endpoint ends the member's sign-in and never the admin
// user's. Without one, the page ends the member's sign-in itself. There is no sign-out by GET.
public sealed class MemberLogoutModel : MemberPageModel
{
    public string? Email { get; private set; }

    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; private set; } = [];

    // Nothing to ask: no member is signed in, and no client waits to be sent back to.
    public bool SignedOut => Email is null && Parameters.Count == 0;

    public async Task OnGetAsync()
    {
        var cookie = await HttpContext.AuthenticateAsync(MemberAuthentication.CookieScheme);
        Email = cookie.Succeeded ? cookie.Principal.Identity?.Name : null;
        Parameters = [.. Request.Query
            .Where(parameter => !string.Equals(parameter.Key, MemberAuthentication.LogoutPageField, StringComparison.OrdinalIgnoreCase))
            .SelectMany(parameter => parameter.Value.Select(value => KeyValuePair.Create(parameter.Key, value ?? "")))];
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await Service<MemberSignInManager>().SignOutAsync();
        return RedirectToPage();
    }
}

// Registers a member, while cms.Members.AllowRegistration is on; off, the page is not there. The answer is the same
// whether or not the address is a member's already.
public sealed class MemberRegisterModel : MemberPageModel
{
    [BindProperty]
    public MemberRegisterInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool Sent { get; private set; }

    public IReadOnlyList<MemberProviderButton> ExternalProviders => ProviderButtons();

    public IActionResult OnGet() => Service<MemberOptions>().AllowRegistration ? Page() : NotFound();

    // A first sign-in with a provider registers, so the page that registers offers the providers too.
    public IActionResult OnPostExternal(string? provider) =>
        Service<MemberOptions>().AllowRegistration
            ? ChallengeProvider(provider, Url.Page("./ExternalLogin", pageHandler: null, values: new { ReturnUrl = LocalUrl(ReturnUrl) })!)
            : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Service<MemberOptions>().AllowRegistration)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await Account.RegisterAsync(Input.Email, Input.Password, Input.DisplayName, HttpContext.RequestAborted);
        }
        catch (ValidationException exception)
        {
            AddErrors(exception);
            return Page();
        }

        Sent = true;
        return Page();
    }
}

public sealed class MemberRegisterInput
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Display(Name = "Name (optional)")]
    public string? DisplayName { get; set; }

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Repeat the password")]
    public string ConfirmPassword { get; set; } = "";
}

// The page the verification mail links to, with the address and the token in the query. Opening the link changes
// nothing: the member presses the button, so that a mail scanner that follows links does not spend the token. Without a
// link, the page sends the mail again, with the same answer whatever the address.
public sealed class MemberVerifyEmailModel : MemberPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Email { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public MemberEmailInput Input { get; set; } = new();

    public bool HasLink => !string.IsNullOrEmpty(Email) && !string.IsNullOrEmpty(Token);

    public bool Sent { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Clear();
        if (!HasLink)
        {
            return Page();
        }

        try
        {
            await Account.VerifyEmailAsync(Email!, Token!, HttpContext.RequestAborted);
        }
        catch (ValidationException exception)
        {
            AddErrors(exception);
            Token = null;
            return Page();
        }

        return RedirectToPage("./Login", new { Status = "email-verified" });
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        Token = null;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await Account.ResendVerificationAsync(Input.Email, HttpContext.RequestAborted);
        Sent = true;
        return Page();
    }
}

// Sends a link to reset the password. The answer is the same whether or not the address belongs to a member.
public sealed class MemberForgotPasswordModel : MemberPageModel
{
    [BindProperty]
    public MemberEmailInput Input { get; set; } = new();

    public bool Sent { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await Account.ForgotPasswordAsync(Input.Email, HttpContext.RequestAborted);
        Sent = true;
        return Page();
    }
}

public sealed class MemberEmailInput
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";
}

// The page the reset mail links to, with the address and the token in the query. It is also where a member an
// administrator created chooses its first password.
public sealed class MemberResetPasswordModel : MemberPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Email { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public NewPasswordInput Input { get; set; } = new();

    public bool HasLink => !string.IsNullOrEmpty(Email) && !string.IsNullOrEmpty(Token);

    public async Task<IActionResult> OnPostAsync()
    {
        if (!HasLink || !ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await Account.ResetPasswordAsync(Email!, Token!, Input.Password, HttpContext.RequestAborted);
        }
        catch (ValidationException exception)
        {
            AddErrors(exception);
            return Page();
        }

        return RedirectToPage("./Login", new { Status = "password-reset" });
    }
}

// Where a provider of the member pool returns to after a sign-in. MemberExternalSignIn decides: a member linked to the
// login signs in, and anyone else gets a member only while registration is open. The external cookie is ended here
// whatever is decided, so the sign-in at the provider is used once.
public sealed class MemberExternalLoginModel : MemberPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ProviderName { get; private set; }

    // A mail went to the address the provider gave, which it did not say is verified; nobody is signed in yet.
    public bool Pending { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var external = Service<MemberExternalSignIn>();
        var login = await external.ReadAsync();
        await external.ForgetAsync();

        // A sign-in that was started to link a login belongs to the page of the member that started it.
        if (login is null || login.LinkFor is not null)
        {
            return RedirectToPage("./Login", new { ReturnUrl = LocalUrl(ReturnUrl), Status = "external-failed" });
        }

        var outcome = await external.SignInAsync(login, HttpContext.RequestAborted);
        if (outcome == MemberExternalOutcome.SignedIn)
        {
            return LocalRedirect(LocalUrl(ReturnUrl));
        }

        ProviderName = login.ProviderName;
        Pending = outcome == MemberExternalOutcome.Pending;
        return Page();
    }
}

// The sign-in methods of the member that is signed in on the member pages: its external logins, which it links and
// removes here. Linking goes to the provider and comes back to this page, which then asks before it links, naming the
// account at the provider, so that the member sees which account will sign in as it.
public sealed class MemberLoginsModel : MemberPageModel
{
    // What the request that sent the member here reports: linked, removed or external-failed.
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public string? Email { get; private set; }

    public IReadOnlyList<MemberLinkedLogin> Logins { get; private set; } = [];

    // The providers the member has no login of yet.
    public IReadOnlyList<MemberProviderButton> Linkable { get; private set; } = [];

    // The account at a provider that waits for the member's word, after the provider sent the member back.
    public MemberPendingLink? PendingLink { get; private set; }

    public string? Notice => Status switch
    {
        "linked" => "The account is linked. You can sign in with it from now on.",
        "removed" => "The sign-in was removed.",
        _ => null,
    };

    public string? Problem => Status == "external-failed" ? "The sign-in with the external provider did not complete. Try again." : null;

    public async Task<IActionResult> OnGetAsync() =>
        await MemberAsync() is { } member ? await ShowAsync(member) : RedirectToLogin();

    public async Task<IActionResult> OnPostLinkAsync(string? provider) =>
        await MemberAsync() is { } member ? ChallengeProvider(provider, Url.Page("./Logins")!, member.Id) : RedirectToLogin();

    public async Task<IActionResult> OnPostConfirmAsync()
    {
        if (await MemberAsync() is not { } member)
        {
            return RedirectToLogin();
        }

        var external = Service<MemberExternalSignIn>();
        var login = await external.ReadAsync();
        await external.ForgetAsync();
        if (login is null || login.LinkFor != member.Id)
        {
            return RedirectToPage(new { Status = "external-failed" });
        }

        var linked = await external.LinkAsync(member, login);
        if (linked.Succeeded)
        {
            return RedirectToPage(new { Status = "linked" });
        }

        foreach (var error in linked.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return await ShowAsync(member, pending: null);
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        await Service<MemberExternalSignIn>().ForgetAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(string? provider)
    {
        if (await MemberAsync() is not { } member)
        {
            return RedirectToLogin();
        }

        try
        {
            await Service<MemberExternalSignIn>().UnlinkAsync(member, provider ?? "", HttpContext.RequestAborted);
        }
        catch (ValidationException exception)
        {
            AddErrors(exception);
            return await ShowAsync(member);
        }

        return RedirectToPage(new { Status = "removed" });
    }

    private async Task<CmsMember?> MemberAsync() => (await Service<MemberSignInManager>().SignedInAsync()).Member;

    private RedirectResult RedirectToLogin() =>
        Redirect(Request.PathBase + HostedPagePaths.MemberLogin + QueryString.Create("ReturnUrl", Url.Page("./Logins")!));

    private async Task<IActionResult> ShowAsync(CmsMember member)
    {
        var login = await Service<MemberExternalSignIn>().ReadAsync();
        return await ShowAsync(member, login?.LinkFor == member.Id ? login : null);
    }

    private async Task<IActionResult> ShowAsync(CmsMember member, MemberExternalLogin? pending)
    {
        var held = await Service<MemberLogins>().ListAsync(member);
        Email = member.Email;
        Logins = [.. held.Select(login => new MemberLinkedLogin(login.Provider, login.DisplayName ?? login.Provider))];
        Linkable = [.. ProviderButtons().Where(button => !held.Any(login => string.Equals(login.Provider, button.Name, StringComparison.OrdinalIgnoreCase)))];
        PendingLink = pending is null ? null : new MemberPendingLink(pending.ProviderName, pending.Email ?? pending.DisplayName);
        return Page();
    }
}

// An external login the member has, as the page lists it.
public sealed record MemberLinkedLogin(string Provider, string DisplayName);

// The account at a provider the member is asked about before it is linked: the address or the name the provider gave.
public sealed record MemberPendingLink(string ProviderName, string? Account);
