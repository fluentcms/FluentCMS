namespace FluentCMS.Api.Plugins.CmsCoreManagement;

[Plugin]
public class CmsCoreManagementPlugin : IPluginStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration? configuration)
    {
        // Register auto-mapper profiles
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<MappingProfile>();
        });

        // Register database context
        services.AddDatabaseContext<CmsCoreDbContext, ICmsCoreDatabaseMarker>();
        services.AddDataSeeder<CmsCoreDataSeeder, ICmsCoreDatabaseMarker>();
        services.AddSchemaValidator<CmsCoreSchemaValidator, ICmsCoreDatabaseMarker>();

        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<IPageRepository, PageRepository>();
        services.AddScoped<ILayoutRepository, LayoutRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IBlockRepository, BlockRepository>();

        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<IPageService, PageService>();
        services.AddScoped<ILayoutService, LayoutService>();
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IBlockService, BlockService>();
    }

    public void Configure(IApplicationBuilder app)
    {
        
    }
}
