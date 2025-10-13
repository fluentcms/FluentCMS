using FluentCMS.Api.Providers.Email.Abstractions;
using FluentCMS.Infrastructure.Providers.Abstractions;

namespace FluentCMS.Api.Providers.Email.Null;

public class NullEmailProviderModule : ProviderModuleBase<NullEmailProvider>
{
    public override string Area => IEmailProvider.Area;

    public override string DisplayName => "Null Email Provider (No-Op)";
}
