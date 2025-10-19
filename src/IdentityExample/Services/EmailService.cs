using IdentityExample.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IdentityExample.Services;

public class EmailService : IEmailService
{
    private readonly SmtpSettings _smtpSettings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<SmtpSettings> smtpSettings, ILogger<EmailService> logger)
    {
        _smtpSettings = smtpSettings.Value;
        _logger = logger;
    }

    public async Task SendEmailConfirmationAsync(string email, string userName, string confirmationToken)
    {
        var subject = "Confirm your email address";
        var body = $@"
            <h2>Welcome to Identity API, {userName}!</h2>
            <p>Please confirm your email address by clicking the link below:</p>
            <p><a href='#'>Confirm Email</a></p>
            <p>Confirmation Token: {confirmationToken}</p>
            <p>If you didn't create this account, please ignore this email.</p>
        ";

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendPasswordResetAsync(string email, string userName, string resetToken)
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

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendWelcomeEmailAsync(string email, string userName)
    {
        var subject = "Welcome to Identity API";
        var body = $@"
            <h2>Welcome to Identity API, {userName}!</h2>
            <p>Your account has been successfully created and confirmed.</p>
            <p>You can now use all the features of our application.</p>
            <p>Thank you for joining us!</p>
        ";

        await SendEmailAsync(email, subject, body);
    }

    private async Task SendEmailAsync(string email, string subject, string body)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_smtpSettings.SenderName, _smtpSettings.SenderEmail));
            message.To.Add(new MailboxAddress("", email));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = body
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            
            // Connect to SMTP server
            await client.ConnectAsync(_smtpSettings.Server, _smtpSettings.Port, 
                _smtpSettings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);

            // Authenticate if credentials are provided
            if (!string.IsNullOrEmpty(_smtpSettings.Username) && !string.IsNullOrEmpty(_smtpSettings.Password))
            {
                await client.AuthenticateAsync(_smtpSettings.Username, _smtpSettings.Password);
            }

            // Send email
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent successfully to {Email} with subject: {Subject}", email, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email} with subject: {Subject}", email, subject);
            throw;
        }
    }
}
