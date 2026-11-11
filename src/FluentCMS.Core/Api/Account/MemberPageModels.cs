using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.Identity.Members;
using Microsoft.AspNetCore.Authentication;
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
}

public sealed class MemberLoginModel : MemberPageModel
{
    // The same answer for every refusal, so that the page does not tell which addresses are members, which are not
    // verified yet and which are locked out.
    internal const string Refused = "Sign-in failed. Check the email address and password. A new account signs in once its address is verified, and after several failed attempts you wait a few minutes before you try again.";

    [BindProperty]
    public MemberLoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    // What the page that sent the member here reports: email-verified or password-reset.
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public bool RegistrationOpen => Service<MemberOptions>().AllowRegistration;

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

    public IActionResult OnGet() => Service<MemberOptions>().AllowRegistration ? Page() : NotFound();

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
