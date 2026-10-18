using FluentCMS.Core.Results;

namespace FluentCMS.Core.Identity;

// M1 sends no email (D25): an administrator issues a reset link and hands it to the person, who sets a new password with it.
public interface IPasswordReset
{
    // Appends an audit event naming the signed-in administrator. A disabled user gets no link.
    Task<Result<PasswordResetToken>> IssueAsync(Guid userId, CancellationToken cancellationToken = default);

    // The token works once and only for an active user. It proves who is resetting, so the audit event names the user.
    Task<Result> CompleteAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken = default);
}

public sealed record PasswordResetToken(string Token, DateTimeOffset ExpiresAt);
