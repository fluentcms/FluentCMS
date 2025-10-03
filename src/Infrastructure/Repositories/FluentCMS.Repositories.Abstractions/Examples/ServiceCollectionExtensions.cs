using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Abstractions.Examples;

public static class TodoServiceCollectionExtensions
{
    // Add UseSqlite method to the connection builder
    public static void AddTodoServices(IServiceCollection services)
    {
        services.AddScoped<ITodoAnotherService, TodoAnotherService>();
        services.AddScoped<ITodoService, TodoService>();
        services.AddScoped<ITodoRepository, TodoRepository>();
    }
}
