using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace FluentCMS.Providers.Email;


public interface IEmailProvider
{
    Task Send(string recipients, string subject, string body, string ccs = null, string bccs = null, CancellationToken cancellationToken = default);
}

public class SmtpEmailProvider : IEmailProvider
{
    protected readonly SmtpEmailProviderOptions Options;
    protected readonly ILogger<SmtpEmailProvider> Logger;

    public SmtpEmailProvider(IOptions<SmtpEmailProviderOptions> options, ILogger<SmtpEmailProvider> logger)
    {
        Options = options.Value;
        Logger = logger;
    }

    public virtual async Task Send(string recipients, string subject, string body, string ccs = null, string bccs = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(recipients))
            throw new ArgumentNullException(nameof(recipients));

        var smtpClient = new SmtpClient(Options.SmtpHost, Options.SmtpPort)
        {
            Credentials = new NetworkCredential(Options.Username, Options.Password),
            EnableSsl = Options.EnableSsl
        };
        var mailMessage = new MailMessage
        {
            From = new MailAddress(Options.MailAddress, Options.DisplayName, Encoding.UTF8),
            Subject = subject,
            Body = body,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true
        };

        mailMessage.To.Add(recipients);

        if (!string.IsNullOrEmpty(ccs))
            mailMessage.CC.Add(ccs);

        if (!string.IsNullOrEmpty(bccs))
            mailMessage.Bcc.Add(bccs);

        await smtpClient.SendMailAsync(mailMessage, cancellationToken);

    }
}

public class SmtpEmailProviderOptions
{
    public required string DisplayName { get; set; }
    public required string MailAddress { get; set; }
    public required string SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
    public bool EnableSsl { get; set; }
}
