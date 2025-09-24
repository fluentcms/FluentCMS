using Microsoft.Extensions.Options;

namespace FluentCMS.Configuration;

public interface IOptionsCatalog
{
    IReadOnlyCollection<OptionRegistration> All { get; }
}

internal sealed class OptionsCatalog(IOptions<ProviderCatalogOptions> options) : IOptionsCatalog
{
    public IReadOnlyCollection<OptionRegistration> All => [.. options.Value.Types];
}
