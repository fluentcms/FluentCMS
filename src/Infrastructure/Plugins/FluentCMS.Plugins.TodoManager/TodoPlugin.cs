namespace FluentCMS.Plugins.TodoManager;

[Plugin]
public class TodoPlugin : IPluginStartup
{
    public void Configure(IApplicationBuilder app)
    {
    }

    public void ConfigureServices(IServiceCollection services, IConfiguration? configuration)
    {
        services.AddDatabaseContext<TodoDbContext, ITodoDatabaseMarker>();
        services.AddDataSeeder<TodoDataSeeder, ITodoDatabaseMarker>();
        services.AddSchemaValidator<TodoSchemaValidator, ITodoDatabaseMarker>();
        services.AddScoped<ITodoService, TodoService>();
        services.AddScoped<ITodoRepository, TodoRepository>();
    }
}
