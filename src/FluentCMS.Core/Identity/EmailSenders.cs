using FluentCMS.Providers;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace FluentCMS.Core.Identity;

// FluentCms:Email:Smtp. A connection per message, which is plenty for invitations and password resets.
internal sealed class SmtpEmailSender(EmailOptions options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Validated at startup: a host is set, and so is a parseable sender.
        var smtp = options.Smtp;
        using var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(smtp.From!));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host!, smtp.Port, SocketOptions(smtp.Security), cancellationToken);
        if (!string.IsNullOrEmpty(smtp.UserName))
        {
            await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? "", cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static SecureSocketOptions SocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.StartTls,
    };
}

// The Development default: the message, link included, goes to the log and nowhere else.
internal sealed partial class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        Sent(logger, message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 9120, Level = LogLevel.Information, Message = "Email to {To}, not sent (Development writes email to the log): {Subject}\n{Body}")]
    private static partial void Sent(ILogger logger, string to, string subject, string body);
}

// Outside Development an email that goes to the log would leak its links, so nothing is sent until a real sender is registered.
internal sealed class UnconfiguredEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"{IdentityErrorCodes.NoEmailSender}: no email sender is registered. Set FluentCms:Email:Smtp or register an IEmailSender; only Development writes email to the log.");
}
