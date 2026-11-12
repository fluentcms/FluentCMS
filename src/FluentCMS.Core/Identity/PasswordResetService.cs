using FluentCMS.Jobs;
using FluentCMS.Providers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity;

// A forgotten password is reset through a link sent by email. Whether an address belongs to a user is never told: an
// unknown address, or one of a deactivated user, gets no email and the same answer, and so does an email that fails to go.
// The request only enqueues the job that sends it, whatever the address, so the time of the answer tells nothing either
// (AccountMail).
internal sealed class PasswordResetService(CmsUserManager users, IEmailSender email, IJobScheduler scheduler, AdminUserOptions options, TimeProvider time, ILogger<PasswordResetService> logger)
{
    private const int MaxAddressLength = 256;

    public async Task RequestAsync(string address, Uri resetUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(resetUrl);

        if (!resetUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("The page that resets a password is linked from an email, so its address is absolute.", nameof(resetUrl));
        }

        var trimmed = address.Trim();
        if (trimmed.Length is > 0 and <= MaxAddressLength)
        {
            await scheduler.EnqueueAsync<PasswordResetMailJob, PasswordResetMailRequest>(new PasswordResetMailRequest(trimmed, resetUrl.AbsoluteUri), AccountMail.Options, cancellationToken);
        }
    }

    // The job's half, which the user reset-password command calls directly: the user of the address, if any, gets the link.
    public async Task SendAsync(string address, Uri resetUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(resetUrl);

        var user = await users.FindByEmailAsync(address);
        if (user is null || user.DeactivatedAt is not null)
        {
            return;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        try
        {
            await email.SendAsync(Message(user, EmailLinks.With(resetUrl, ("user", user.Id.ToString("D")), ("token", token)), time.GetUtcNow() + options.PasswordResetLifetime), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AccountLog.ResetEmailFailed(logger, user.Id, exception);
        }
    }

    public async Task<IdentityResult> ResetAsync(Guid userId, string token, string password)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(password);

        var user = await users.FindByIdAsync(userId.ToString("D"));
        return user is null || user.DeactivatedAt is not null
            ? IdentityResult.Failed(users.ErrorDescriber.InvalidToken())
            : await users.ResetPasswordAsync(user, token, password);
    }

    private static EmailMessage Message(CmsUser user, Uri link, DateTimeOffset expiresAt) =>
        new(
            user.Email!,
            "Reset your FluentCMS password",
            $"Someone asked to reset the password of {user.UserName} in the FluentCMS admin. Choose a new password here:\n\n{link.AbsoluteUri}\n\nThe link can be used once and expires at {expiresAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC. If you did not ask for it, ignore this email; your password stays as it is.");
}

internal sealed record PasswordResetMailRequest(string Email, string ResetUrl);

internal sealed class PasswordResetMailJob(PasswordResetService resets) : IJob<PasswordResetMailRequest>
{
    public const string Type = "users.send_password_reset";

    public Task ExecuteAsync(PasswordResetMailRequest payload, JobContext jobContext)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(jobContext);

        return resets.SendAsync(payload.Email, new Uri(payload.ResetUrl, UriKind.Absolute), jobContext.CancellationToken);
    }
}
