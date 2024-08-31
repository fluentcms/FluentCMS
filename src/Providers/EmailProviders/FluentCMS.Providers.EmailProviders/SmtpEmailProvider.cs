using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace FluentCMS.Providers.EmailProviders;

public class SmtpEmailProvider : IEmailProvider
{
    private readonly SmtpServerConfig _smtpServerConfiguration;

    public SmtpEmailProvider(IOptions<SmtpServerConfig> smtpServerOptions)
    {
        _smtpServerConfiguration = smtpServerOptions.Value;
    }

    public async Task Send(string from, string recipients, string? subject, string? body, CancellationToken cancellationToken = default)
    {
        try
        {
            // Create SMTP client
            var client = new SmtpClient(_smtpServerConfiguration.Server, _smtpServerConfiguration.Port)
            {
                EnableSsl = _smtpServerConfiguration.EnableSsl,
                Credentials = new NetworkCredential(_smtpServerConfiguration.Username, _smtpServerConfiguration.Password)
            };

            // Create email message
            var mailMessage = new MailMessage
            {
                From = new MailAddress(from),
                Subject = subject,
                IsBodyHtml = true,
                Body = body,
            };

            // Add recipients
            foreach (var recipient in recipients.Split(','))
            {
                mailMessage.To.Add(recipient);
            }

            // Send email
            await client.SendMailAsync(mailMessage, cancellationToken);
        }
        catch (Exception)
        {
        }
    }

    public async Task Send(string recipients, string? subject, string? body, CancellationToken cancellationToken = default)
    {
        await Send(_smtpServerConfiguration.From, recipients, subject, body, cancellationToken);
    }
}
