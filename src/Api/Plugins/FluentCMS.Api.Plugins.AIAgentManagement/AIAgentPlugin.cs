namespace FluentCMS.Api.Plugins.AIAgentManagement;

[Plugin]
public class AIAgentPlugin : IPluginStartup
{
    public void Configure(IApplicationBuilder app)
    {
    }

    public void ConfigureServices(IServiceCollection services, IConfiguration? configuration)
    {
        //services.AddDatabaseContext<AIDbContext, IAIAgentDatabaseMarker>();
        services.AddScoped<Tools>();
        services.AddOpenRouterChatClient(configuration);
    }
}
