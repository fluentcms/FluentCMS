using FluentCMS.Api.Providers.Caching.Abstractions;
using FluentCMS.Infrastructure.Providers.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Api.Providers.Caching.InMemory;

public class InMemoryCacheProviderModule : ProviderModuleBase<InMemoryCacheProvider>
{
    public override string Area => ICacheProvider.Area;
    public override string DisplayName => "In-Memory Cache Provider";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddMemoryCache();
    }
}
