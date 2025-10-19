namespace FluentCMS.Api.Plugins.CmsCoreManagement;

[Plugin]
public class CmsCoreManagementPlugin : IPluginStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration? configuration)
    {
        // Register database context
        services.AddDatabaseContext<CmsCoreDbContext, ICmsCoreDatabaseMarker>();
        services.AddSchemaValidator<CmsCoreSchemaValidator, ICmsCoreDatabaseMarker>();

        services.AddScoped<ISiteRepository, SiteRepository>();

        services.AddScoped<ISiteService, SiteService>();
    }

    public void Configure(IApplicationBuilder app)
    {
        
    }
}
