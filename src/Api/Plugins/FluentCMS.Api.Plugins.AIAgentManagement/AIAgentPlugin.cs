namespace FluentCMS.Api.Plugins.AIAgentManagement;

[Plugin]
public class AIAgentPlugin : IPluginStartup
{
    public void Configure(IApplicationBuilder app)
    {
    }

    public void ConfigureServices(IServiceCollection services, IConfiguration? configuration)
    {
        services.AddDatabaseContext<AIDbContext, IAIAgentDatabaseMarker>();
        services.AddDataSeeder<AgentDataSeeder, IAIAgentDatabaseMarker>();
        services.AddSchemaValidator<AgentSchemaValidator, IAIAgentDatabaseMarker>();
        services.AddScoped<Tools>();
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IThreadRepository, ThreadRepository>();
        services.AddOpenRouterChatClient(configuration);
    }
}
