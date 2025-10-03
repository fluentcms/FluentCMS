namespace FluentCMS.Repositories.Abstractions.Examples;

public interface ITodoRepository : IRepository<TodoItem>
{
}

internal class TodoRepository(ITodoDataContext dataContext) : BaseRepository<TodoItem, ITodoDataContext>(dataContext), ITodoRepository
{
}
