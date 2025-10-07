using FluentCMS.EventBus.Abstractions;

namespace FluentCMS.Plugins.AuditTrailManager;

public class AuditTrailManagerPlugin : IPlugin
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        var services = builder.Services;

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
