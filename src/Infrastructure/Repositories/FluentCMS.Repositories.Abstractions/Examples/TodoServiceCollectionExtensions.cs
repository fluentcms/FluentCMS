using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Abstractions.Examples;

public static class TodoServiceCollectionExtensions
{
    public static void AddTodoServices(IServiceCollection services)
    {
        services.AddScoped<ITodoRepository, TodoRepository>();
        services.AddScoped<ITodoAnotherService, TodoAnotherService>();
        services.AddScoped<ITodoService, TodoService>();
        services.AddScoped<IDataSeeder, TodoDataSeeder>();
    }
}
