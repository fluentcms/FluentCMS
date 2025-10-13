namespace FluentCMS.Api.Plugins.AuditTrailManagement;

[Plugin]
public class AuditTrailManagerPlugin : IPluginStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration? configuration)
    {
        services.AddSchemaValidator<AuditTrailSchemaValidator, IAuditTrailDatabaseMarker>();
        // TODO: remove interception for eventbus and audit entity
        services.AddDatabaseContext<AuditTrailDbContext, IAuditTrailDatabaseMarker>();
        services.AddEventHandler<RepositoryEntityCreatedEvent, AuditTrailHandler>();
        services.AddEventHandler<RepositoryEntityUpdatedEvent, AuditTrailHandler>();
        services.AddEventHandler<RepositoryEntityDeletedEvent, AuditTrailHandler>();

        services.AddScoped<IAuditTrailService, AuditTrailService>();
        services.AddScoped<IAuditTrailRepository, AuditTrailRepository>();
        services.AddAutoMapper(cfg => { }, typeof(MappingProfile));
    }

    public void Configure(IApplicationBuilder app)
    {

    }
}
