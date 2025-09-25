using FluentCMS.Providers.Abstractions;
using FluentCMS.Providers.Email.Abstractions;

namespace FluentCMS.Providers.Email.Null;

public class NullEmailProviderModule : ProviderModuleBase<NullEmailProvider>
{
    public override string Area => IEmailProvider.Area;

    public override string DisplayName => "Null Email Provider (No-Op)";
}
