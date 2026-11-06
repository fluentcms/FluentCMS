using FluentCMS.Providers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity;

// A forgotten password is reset through a link sent by email. Whether an address belongs to a user is never told: an
// unknown address, or one of a deactivated user, gets no email and the same answer, and so does an email that fails to go.
internal sealed class PasswordResetService(CmsUserManager users, IEmailSender email, AdminUserOptions options, TimeProvider time, ILogger<PasswordResetService> logger)
{
    public async Task RequestAsync(string address, Uri resetUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(resetUrl);

        if (!resetUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("The page that resets a password is linked from an email, so its address is absolute.", nameof(resetUrl));
        }

        var user = await users.FindByEmailAsync(address.Trim());
        if (user is null || user.DeactivatedAt is not null)
        {
            return;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        try
        {
            await email.SendAsync(Message(user, Link(resetUrl, user.Id, token), time.GetUtcNow() + options.PasswordResetLifetime), cancellationToken);
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

    private static Uri Link(Uri resetUrl, Guid userId, string token)
    {
        var builder = new UriBuilder(resetUrl);
        var query = builder.Query.TrimStart('?');
        builder.Query = (query.Length == 0 ? "" : query + "&") + "user=" + userId.ToString("D") + "&token=" + Uri.EscapeDataString(token);
        return builder.Uri;
    }

    private static EmailMessage Message(CmsUser user, Uri link, DateTimeOffset expiresAt) =>
        new(
            user.Email!,
            "Reset your FluentCMS password",
            $"Someone asked to reset the password of {user.UserName} in the FluentCMS admin. Choose a new password here:\n\n{link.AbsoluteUri}\n\nThe link can be used once and expires at {expiresAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC. If you did not ask for it, ignore this email; your password stays as it is.");
}
