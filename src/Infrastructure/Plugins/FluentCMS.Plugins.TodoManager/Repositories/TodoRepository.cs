using FluentCMS.Plugins.TodoManager.Models;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;

namespace FluentCMS.Plugins.TodoManager.Repositories;

public interface ITodoRepository : IRepository<Todo>
{
}

internal class TodoRepository(TodoDbContext dbContext) : Repository<Todo, TodoDbContext>(dbContext), ITodoRepository
{
}
