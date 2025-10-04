using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Abstractions.Examples;

public static class TodoServiceCollectionExtensions
{
    public static void AddTodoServices(IServiceCollection services)
    {
        // Register custom DbContext for Todo area with SQL Server configuration
        services.AddDataContextForArea<ITodoDatabaseMarker, TodoDbContext>(options =>
            options.ConfigureWarnings(warnings => warnings.Throw())
        );

        services.AddDataSeeder<TodoDataSeeder, ITodoDatabaseMarker>();
        services.AddScoped<ITodoRepository, TodoRepository>();
        services.AddScoped<ITodoAnotherService, TodoAnotherService>();
        services.AddScoped<ITodoService, TodoService>();
    }
}
