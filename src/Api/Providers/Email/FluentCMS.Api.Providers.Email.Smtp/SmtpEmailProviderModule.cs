using FluentCMS.Api.Providers.Email.Abstractions;
using FluentCMS.Infrastructure.Providers.Abstractions;

namespace FluentCMS.Api.Providers.Email.Smtp;

public class SmtpEmailProviderModule : ProviderModuleBase<SmtpEmailProvider, SmtpEmailProviderOptions>
{
    public override string Area => IEmailProvider.Area;

    public override string DisplayName => "SMTP Email Provider";
}
