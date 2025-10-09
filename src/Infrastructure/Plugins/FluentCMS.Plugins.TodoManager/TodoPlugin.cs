namespace FluentCMS.Plugins.TodoManager;

public class TodoPlugin : IPlugin
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        builder.Services.AddDatabaseContext<TodoDbContext, ITodoDatabaseMarker>();
        builder.Services.AddDataSeeder<TodoDataSeeder, ITodoDatabaseMarker>();
        builder.Services.AddSchemaValidator<TodoSchemaValidator, ITodoDatabaseMarker>();
        builder.Services.AddScoped<ITodoService, TodoService>();
        builder.Services.AddScoped<ITodoRepository, TodoRepository>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }
}
