namespace FluentCMS.Repositories.Abstractions.Examples;

public interface ITodoService
{
    Task<TodoItem> Create(string title, string description);
    Task<TodoItem> Delete(TodoItem todoItem);
    Task<TodoItem> Update(TodoItem todoItem);
}

public class TodoService(ITodoRepository todoRepository) : ITodoService
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
        return await todoRepository.Add(todoItem);
    }

    public async Task<TodoItem> Update(TodoItem todoItem)
    {
        return await todoRepository.Update(todoItem);
    }
    public async Task<TodoItem> Delete(TodoItem todoItem)
    {
        return await todoRepository.Remove(todoItem);
    }
}

