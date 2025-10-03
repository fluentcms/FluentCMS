using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Abstractions.Examples;

public static class TodoServiceCollectionExtensions
{
    public static void AddTodoServices(IServiceCollection services)
    {
        services.AddDataContextForArea<ITodoDatabaseMarker>();
        services.AddScoped<ITodoAnotherService, TodoAnotherService>();
        services.AddScoped<ITodoService, TodoService>();
        services.AddScoped<ITodoRepository, TodoRepository>();
    }
}
