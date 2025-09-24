using FluentCMS.Providers.Abstractions;
using FluentCMS.Providers.Email.Abstractions;

namespace FluentCMS.Providers.Email.Smtp;

public class SmtpEmailProviderModule : ProviderModuleBase<SmtpEmailProvider, SmtpEmailProviderOptions>
{
    public override string Area => IEmailProvider.Area;

    public override string DisplayName => "SMTP Email Provider";
}
