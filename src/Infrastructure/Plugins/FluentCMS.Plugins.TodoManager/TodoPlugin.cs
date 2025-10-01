using FluentCMS.Plugins.Abstractions;
using FluentCMS.Plugins.TodoManager.Repositories;
using FluentCMS.Plugins.TodoManager.Services;
using FluentCMS.Repositories.EntityFramework.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Plugins.TodoManager;

public class TodoPlugin : IPlugin
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        builder.Services.AddDatabaseContext<TodoDbContext>();
        builder.Services.AddDataSeeder<TodoDataSeeder, ITodoDatabaseMarker>();
        builder.Services.AddSchemaValidator<TodoSchemaValidator, ITodoDatabaseMarker>();
        builder.Services.AddScoped<ITodoService, TodoService>();
        builder.Services.AddScoped<ITodoRepository, TodoRepository>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }
}
