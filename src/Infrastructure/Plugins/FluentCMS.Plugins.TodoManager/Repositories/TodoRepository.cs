using FluentCMS.Plugins.TodoManager.Models;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.TodoManager.Repositories;

public interface ITodoRepository : IRepository<Todo>
{
}

internal class TodoRepository(TodoDbContext dbContext, ILogger<TodoRepository> logger) : EfRepository<Todo, TodoDbContext>(dbContext, logger), ITodoRepository
{
}
