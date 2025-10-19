namespace FluentCMS.Api.Plugins.IdentityManagement.Services;

public interface IEmailSender
{
    Task SendConfirmation(string email, string userName, string confirmationToken, CancellationToken cancellationToken = default);
    Task SendPasswordReset(string email, string userName, string resetToken, CancellationToken cancellationToken = default);
    Task SendWelcomeEmail(string email, string userName, CancellationToken cancellationToken = default);
}

internal class EmailSender(IEmailProvider emailProvider) : IEmailSender
{
    public async Task SendConfirmation(string email, string userName, string confirmationToken, CancellationToken cancellationToken = default)
    {
        var subject = "Confirm your email address";
        var body = $@"
            <h2>Welcome to Identity API, {userName}!</h2>
            <p>Please confirm your email address by clicking the link below:</p>
            <p><a href='#'>Confirm Email</a></p>
            <p>Confirmation Token: {confirmationToken}</p>
            <p>If you didn't create this account, please ignore this email.</p>
        ";

        await emailProvider.Send(email, subject, body, null, cancellationToken);
    }

    public async Task SendPasswordReset(string email, string userName, string resetToken, CancellationToken cancellationToken = default)
    {
        var subject = "Password Reset Request";
        var body = $@"
            <h2>Password Reset Request</h2>
            <p>Hello {userName},</p>
            <p>You have requested to reset your password. Use the token below to reset your password:</p>
            <p>Reset Token: {resetToken}</p>
            <p>This token will expire in 15 minutes for security reasons.</p>
            <p>If you didn't request this password reset, please ignore this email.</p>
        ";

        await emailProvider.Send(email, subject, body, null, cancellationToken);
    }

    public async Task SendWelcomeEmail(string email, string userName, CancellationToken cancellationToken = default)
    {
        var subject = "Welcome to Identity API";
        var body = $@"
            <h2>Welcome to Identity API, {userName}!</h2>
            <p>Your account has been successfully created and confirmed.</p>
            <p>You can now use all the features of our application.</p>
            <p>Thank you for joining us!</p>
        ";

        await emailProvider.Send(email, subject, body, null, cancellationToken);
    }
}
