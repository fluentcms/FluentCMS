namespace FluentCMS.Infrastructure.Providers;

public sealed class ProviderFeatureBuilder
{
    public IServiceCollection Services { get; }

    internal ProviderFeatureBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
