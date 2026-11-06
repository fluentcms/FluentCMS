using FluentCMS.Core.DependencyInjection;
using MimeKit;

namespace FluentCMS.Core.Identity;

// FluentCms:Email. With an SMTP host set, email goes through it in every environment.
public sealed class EmailOptions
{
    public const string Section = "Email";

    public SmtpOptions Smtp { get; } = new();

    internal bool UsesSmtp => !string.IsNullOrWhiteSpace(Smtp.Host);

    internal void Validate()
    {
        if (!UsesSmtp)
        {
            return;
        }

        if (Smtp.Port is < 1 or > 65535)
        {
            throw new FluentCmsConfigurationException(HostErrorCodes.InvalidOption, $"{nameof(Smtp)}:{nameof(SmtpOptions.Port)} is {Smtp.Port}; it must be from 1 to 65535.");
        }

        if (!Enum.IsDefined(Smtp.Security))
        {
            throw new FluentCmsConfigurationException(HostErrorCodes.InvalidOption, $"{nameof(Smtp)}:{nameof(SmtpOptions.Security)} is {Smtp.Security}; use StartTls, SslOnConnect or None.");
        }

        if (string.IsNullOrWhiteSpace(Smtp.From) || !MailboxAddress.TryParse(Smtp.From, out _))
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{nameof(Smtp)}:{nameof(SmtpOptions.From)} is '{Smtp.From}'; it must be the sender's address, such as cms@example.com or \"FluentCMS <cms@example.com>\".");
        }
    }
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    // Without a user name the server is not asked to authenticate the sender.
    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string? From { get; set; }
}

public enum SmtpSecurity
{
    // Plain on connect, then TLS before anything is sent; a server that does not offer it is refused.
    StartTls,

    // TLS from the first byte, as on port 465.
    SslOnConnect,

    // No TLS, for a relay on a trusted network only: the messages and the password cross it in the clear.
    None,
}
