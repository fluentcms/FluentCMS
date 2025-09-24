using FluentCMS.Providers.Abstractions;

namespace FluentCMS.Providers;

public class ProviderCatalog(IProviderModule module, string providerName, bool active, object? options = null)
{
    public string Name { get; } = providerName;
    public bool Active { get; } = active;
    public IProviderModule Module { get; } = module;
    public object? Options { get; } = options;
}
