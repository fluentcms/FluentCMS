using FluentCMS.DataSeeding;
using FluentCMS.Plugins.Abstractions;
using FluentCMS.Plugins.TodoManagement.Models;
using FluentCMS.Plugins.TodoManager.Repositories;
using FluentCMS.Plugins.TodoManager.Services;
using FluentCMS.Repositories.EntityFramework;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Plugins.TodoManager;

public class TodoPlugin : IPlugin
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {

        builder.Services.AddSchemaValidator<TodoSchemaValidator>();
        builder.Services.AddDataSeeder<TodoDataSeeder>();

        builder.Services.AddScoped<ITodoService, TodoService>();
        builder.Services.AddGenericRepository<Todo, TodoDbContext>();
        builder.Services.AddEfDbContext<TodoDbContext>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }
}