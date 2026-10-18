using System.Security.Claims;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Host.Identity;

// Signs people in with Identity's application cookie (ADR-0009). Each sign-in and sign-out is an audit event naming the user.
internal sealed class CookieUserSignIn(
    SignInManager<FluentCmsUser> signInManager,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IHttpContextAccessor accessor) : IUserSignIn
{
    private const string SubjectType = "user";

    private static readonly Result Failed = Result.Failure("sign_in_failed", "The email or password is incorrect, or the account is locked.");

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

    private HttpContext HttpContext => accessor.HttpContext
        ?? throw new InvalidOperationException("Cookie sign-in runs inside an HTTP request.");
}
