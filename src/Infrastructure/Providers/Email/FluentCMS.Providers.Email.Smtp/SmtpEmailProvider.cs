using FluentCMS.Providers.Email.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace FluentCMS.Providers.Email.Smtp;

public class SmtpEmailProvider(IOptions<SmtpEmailProviderOptions> smtpOptionsAccessor, SmtpEmailProviderOptions smtpEmailProviderOptions) : IEmailProvider
{
    public readonly SmtpEmailProviderOptions SmtpEmailProviderOptions = smtpEmailProviderOptions;

    public async Task Send(string recipient, string subject, string body, IDictionary<string, string>? headers = null)
    {
        var options = smtpOptionsAccessor.Value;
        using var smtpClient = new SmtpClient(options.Host, options.Port)
        {
            Credentials = new NetworkCredential(options.Username, options.Password),
            EnableSsl = options.EnableSsl
        };

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(options.FromAddress, options.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = options.IsBodyHtml
        };

        mailMessage.To.Add(recipient);

        if (headers != null)
        {
            foreach (var header in headers)
            {
                mailMessage.Headers.Add(header.Key, header.Value);
            }
        }

        await smtpClient.SendMailAsync(mailMessage);
    }
}