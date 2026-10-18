using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Identity;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FluentCMS.Host.Identity;

// Reset tokens come from Identity's data protection provider. Each is bound to the security stamp, which the reset changes, so it works once.
internal sealed class IdentityPasswordReset(
    SignInManager<FluentCmsUser> signInManager,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    IHttpContextAccessor accessor,
    IOptions<DataProtectionTokenProviderOptions> tokenOptions) : IPasswordReset
{
    public const string LifespanSetting = "PasswordReset:TokenLifespan";

    private const string SubjectType = "user";

    // One answer for an unknown user, a disabled one and a wrong, used or expired token, so whoever holds a link learns nothing more.
    private static readonly Result InvalidLink = Result.Failure(
        UserErrorCodes.ResetLinkInvalid,
        "The reset link is not valid. It may have been used already or have expired, or the account may be disabled.");

    public async Task<Result<PasswordResetToken>> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var users = signInManager.UserManager;
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result<PasswordResetToken>.Failure(UserErrorCodes.UserNotFound, $"No user has the id {userId}.");
        }

        if (Accounts.IsDisabled(user))
        {
            return Result<PasswordResetToken>.Failure(UserErrorCodes.UserDisabled, "A disabled user gets no reset link. Enable them first.");
        }

        // The provider dates the token by the system clock, not TimeProvider, so the expiry shown is read from the same clock.
        var expiresAt = DateTimeOffset.UtcNow + tokenOptions.Value.TokenLifespan;

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await auditLog.AppendAsync(new NewAuditEvent("user.password_reset_issued") { SubjectType = SubjectType, SubjectId = user.Id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Result<PasswordResetToken>.Success(new PasswordResetToken(token, expiresAt));
    }

    public async Task<Result> CompleteAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var users = signInManager.UserManager;
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || Accounts.IsDisabled(user))
        {
            return InvalidLink;
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Identity checks the token before the password, so a rejected password leaves the link usable for another try.
        var reset = await users.ResetPasswordAsync(user, token, newPassword);

        // The person proved who they are, so the lockout their forgotten-password attempts may have caused no longer applies.
        if (reset.Succeeded && await users.IsLockedOutAsync(user))
        {
            reset = await users.SetLockoutEndDateAsync(user, null);
        }

        if (reset.Succeeded)
        {
            reset = await users.ResetAccessFailedCountAsync(user);
        }

        if (!reset.Succeeded)
        {
            return reset.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? InvalidLink
                : Result.Failure(Accounts.ToErrors(reset));
        }

        // As after a password check, the rest of this request acts as the user and the log names them as initiator.
        HttpContext.User = await signInManager.CreateUserPrincipalAsync(user);
        await auditLog.AppendAsync(new NewAuditEvent("user.password_reset") { SubjectType = SubjectType, SubjectId = user.Id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private HttpContext HttpContext => accessor.HttpContext
        ?? throw new InvalidOperationException("Completing a password reset runs inside an HTTP request.");
}
