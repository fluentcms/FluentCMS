using System.Security.Claims;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FluentCMS.Host.Identity;

// Signs people in with Identity's application cookie (ADR-0009). Each sign-in and sign-out is an audit event naming the user.
internal sealed class CookieUserSignIn(
    SignInManager<FluentCmsUser> signInManager,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IHttpContextAccessor accessor,
    IOptions<OpenIdConnectProviderOptions> providerOptions) : IUserSignIn
{
    private const string SubjectType = "user";

    private static readonly Result Failed = Result.Failure("sign_in_failed", "The email or password is incorrect, or the account is locked.");

    private static readonly Result ExternalFailed = Result.Failure(
        "external_sign_in_failed",
        "The identity provider's account matches no active user with the same verified email.");

    public async Task<Result> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await signInManager.UserManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Hashing anyway makes an unknown email as slow as a wrong password, so the timing does not reveal which accounts exist.
            signInManager.UserManager.PasswordHasher.HashPassword(new FluentCmsUser(), password);
            return Failed;
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // A failure still commits, because the failed-attempt count it raised is what locks the account out.
        var check = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            await scope.CommitAsync(cancellationToken);
            return Failed;
        }

        // The password is verified, so the rest of this request acts as the user and the log names them as initiator.
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        HttpContext.User = principal;
        await auditLog.AppendAsync(new NewAuditEvent("user.signed_in") { SubjectType = SubjectType, SubjectId = user.Id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        // The cookie goes out only once the event has committed.
        await signInManager.SignInWithClaimsAsync(user, isPersistent: false, [new Claim("amr", "pwd")]);
        return Result.Success();
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (ClaimsInitiator.From(HttpContext.User) is UserInitiator user)
        {
            await using var scope = await unitOfWork.BeginAsync(cancellationToken);
            await auditLog.AppendAsync(new NewAuditEvent("user.signed_out") { SubjectType = SubjectType, SubjectId = user.UserId }, cancellationToken);
            await scope.CommitAsync(cancellationToken);
        }

        await signInManager.SignOutAsync();
    }

    public async Task<IReadOnlyList<ExternalProvider>> GetExternalProvidersAsync(CancellationToken cancellationToken = default)
    {
        var schemes = await signInManager.GetExternalAuthenticationSchemesAsync();
        return schemes.Select(scheme => new ExternalProvider(scheme.Name, scheme.DisplayName ?? scheme.Name)).ToList();
    }

    public async Task<Result> SignInExternalAsync(CancellationToken cancellationToken = default)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();

        // The external cookie has done its job whatever happens next.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        if (info?.Principal.FindFirst(ClaimTypes.NameIdentifier) is not { } subject)
        {
            return ExternalFailed;
        }

        // A subject is unique only at the issuer that assigned it, so the link records both.
        var login = new UserLoginInfo(subject.Issuer, subject.Value, info.ProviderDisplayName);
        var users = signInManager.UserManager;
        var user = await users.FindByLoginAsync(login.LoginProvider, login.ProviderKey);
        var isNewLink = user is null;
        if (user is null && VerifiedEmail(info.Principal) is { } email)
        {
            user = await users.FindByEmailAsync(email);
        }

        if (user is null || await users.IsLockedOutAsync(user) || !await signInManager.CanSignInAsync(user))
        {
            return ExternalFailed;
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        if (isNewLink && !(await users.AddLoginAsync(user, login)).Succeeded)
        {
            return ExternalFailed;
        }

        HttpContext.User = await signInManager.CreateUserPrincipalAsync(user);
        if (isNewLink)
        {
            await auditLog.AppendAsync(new NewAuditEvent("user.login_linked") { SubjectType = SubjectType, SubjectId = user.Id }, cancellationToken);
        }

        await auditLog.AppendAsync(new NewAuditEvent("user.signed_in") { SubjectType = SubjectType, SubjectId = user.Id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        await signInManager.SignInWithClaimsAsync(user, isPersistent: false, [new Claim(ClaimTypes.AuthenticationMethod, info.LoginProvider)]);
        return Result.Success();
    }

    // Linking by email is only as safe as the provider's word that the address belongs to the person signing in.
    private string? VerifiedEmail(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var verified = providerOptions.Value.AssumeVerifiedEmail
            || string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);

        return verified && !string.IsNullOrWhiteSpace(email) ? email : null;
    }

    private HttpContext HttpContext => accessor.HttpContext
        ?? throw new InvalidOperationException("Cookie sign-in runs inside an HTTP request.");
}
