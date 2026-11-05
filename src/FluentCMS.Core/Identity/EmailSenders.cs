using FluentCMS.Providers;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity;

// The Development default: the message, link included, goes to the log and nowhere else.
internal sealed partial class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        Sent(logger, message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email to {To}, not sent (Development writes email to the log): {Subject}\n{Body}")]
    private static partial void Sent(ILogger logger, string to, string subject, string body);
}

// Outside Development an email that goes to the log would leak its links, so nothing is sent until a real sender is registered.
internal sealed class UnconfiguredEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"{IdentityErrorCodes.NoEmailSender}: no email sender is registered. Register an IEmailSender; only Development writes email to the log.");
}
