using FluentCMS.Providers.Abstractions;
using FluentCMS.Providers.Caching.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Providers.Caching.InMemory;

public class InMemoryCacheProviderModule : ProviderModuleBase<InMemoryCacheProvider>
{
    public override string Area => ICacheProvider.Area;
    public override string DisplayName => "In-Memory Cache Provider";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddMemoryCache();
    }
}
