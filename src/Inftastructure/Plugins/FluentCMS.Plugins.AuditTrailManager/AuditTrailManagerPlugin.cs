namespace FluentCMS.Plugins.AuditTrailManager;

public class AuditTrailManagerPlugin : IPlugin
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddSchemaValidator<AuditTrailSchemaValidator>();

        services.AddDbContext<AuditTrailDbContext>((provider, options) =>
        {
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            var dbConfig = provider.GetRequiredService<IDatabaseConfiguration>();
            dbConfig.ConfigureDbContext(options);
        });

        services.AddTransient<IEventSubscriber<RepositoryEntityCreatedEvent>, AuditTrailHandler>();
        services.AddTransient<IEventSubscriber<RepositoryEntityUpdatedEvent>, AuditTrailHandler>();
        services.AddTransient<IEventSubscriber<RepositoryEntityRemovedEvent>, AuditTrailHandler>();

        services.AddScoped<IAuditTrailService, AuditTrailService>();
        services.AddScoped<IAuditTrailRepository, AuditTrailRepository>();
        services.AddAutoMapper(cfg => { }, typeof(MappingProfile));
    }

    public void Configure(IApplicationBuilder app)
    {

    }
}
