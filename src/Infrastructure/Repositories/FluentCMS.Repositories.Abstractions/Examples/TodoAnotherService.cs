namespace FluentCMS.Repositories.Abstractions.Examples;

public interface ITodoAnotherService
{
    Task<TodoItem> Create(string title, string description);
    Task<TodoItem> Delete(TodoItem todoItem);
    Task<TodoItem> Update(TodoItem todoItem);
}

internal class TodoAnotherService(BaseRepository<TodoItem, ITodoDataContext> repository) : ITodoAnotherService
{
    public async Task<TodoItem> Create(string title, string description)
    {
        var todoItem = new TodoItem
        {
            Title = title,
            Description = description,
            IsCompleted = false,
            CreatedAt = DateTime.UtcNow
        };
        return await repository.Add(todoItem);
    }

    public async Task<TodoItem> Update(TodoItem todoItem)
    {
        return await repository.Update(todoItem);
    }
    public async Task<TodoItem> Delete(TodoItem todoItem)
    {
        return await repository.Remove(todoItem);
    }
}
