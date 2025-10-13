namespace FluentCMS.Infrastructure.Providers.Repositories.EntityFramework;

public static class ProviderFeatureBuilderExtensions
{
    public static ProviderFeatureBuilder UseEntityFramework(this ProviderFeatureBuilder providerFeatureBuilder)
    {
        providerFeatureBuilder.Services.AddScoped<ConfigurationReadOnlyProviderRepository>();
        providerFeatureBuilder.Services.AddDataSeeder<ProviderDataSeeder, IProviderDatabaseMarker>();
        providerFeatureBuilder.Services.AddSchemaValidator<ProviderSchemaValidator, IProviderDatabaseMarker>();

        providerFeatureBuilder.Services.AddDatabaseContext<ProviderDbContext, IProviderDatabaseMarker>();
        providerFeatureBuilder.Services.AddScoped<IProviderRepository, ProviderRepository>();
        return providerFeatureBuilder;
    }
}
