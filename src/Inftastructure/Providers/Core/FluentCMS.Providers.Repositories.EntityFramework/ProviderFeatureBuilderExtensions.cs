using FluentCMS.DataSeeding;
using FluentCMS.Providers.Repositories.Abstractions;
using FluentCMS.Providers.Repositories.Configuration;
using FluentCMS.Repositories.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Providers.Repositories.EntityFramework;

public static class ProviderFeatureBuilderExtensions
{
    public static ProviderFeatureBuilder UseEntityFramework(this ProviderFeatureBuilder providerFeatureBuilder)
    {
        providerFeatureBuilder.Services.AddScoped<ConfigurationReadOnlyProviderRepository>();
        providerFeatureBuilder.Services.AddDataSeeder<ProviderDataSeeder>();
        providerFeatureBuilder.Services.AddSchemaValidator<ProviderSchemaValidator>();

        providerFeatureBuilder.Services.AddEfDbContext<ProviderDbContext>();
        providerFeatureBuilder.Services.AddScoped<IProviderRepository, ProviderRepository>();
        return providerFeatureBuilder;
    }
}
