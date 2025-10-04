namespace FluentCMS.Repositories.Abstractions.Examples;

public interface ITodoRepository : IRepository<TodoItem>
{
}

internal class TodoRepository(TodoDbContext dataContext) : Repository<TodoItem, TodoDbContext>(dataContext), ITodoRepository
{
}
