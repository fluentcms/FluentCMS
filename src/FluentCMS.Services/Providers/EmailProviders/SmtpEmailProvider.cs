using System.Net.Mail;
using System.Net;

namespace FluentCMS.Providers;

public interface ISmtpEmailProvider
{
    Task Send(string from, string recipients, string? subject, string? body, CancellationToken cancellationToken = default);
}

public class SmtpEmailProvider(SmtpServerConfiguration smtpServerConfiguration) : ISmtpEmailProvider
{
    public async Task Send(string from, string recipients, string? subject, string? body, CancellationToken cancellationToken = default)
    {
        try
        {
            // Create SMTP client
            var client = new SmtpClient(smtpServerConfiguration.Server, smtpServerConfiguration.Port)
            {
                EnableSsl = smtpServerConfiguration.EnableSsl,
                //DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrEmpty(smtpServerConfiguration.Username))
            {
                //client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(smtpServerConfiguration.Username, smtpServerConfiguration.Password);
            }

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
        catch (Exception ex)
        {
            var x = ex;
        }
    }
}
