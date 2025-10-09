namespace FluentCMS.Plugins.TodoManager.Services;

public interface ITodoService
{
    Task<Todo> Add(Todo entity, CancellationToken cancellationToken = default);
    Task<List<Todo>> GetAll(CancellationToken cancellationToken = default);
    Task<Todo?> GetById(Guid entityId, CancellationToken cancellationToken = default);
    Task Remove(Guid entityId, CancellationToken cancellationToken = default);
    Task<Todo> Update(Todo entity, CancellationToken cancellationToken = default);
}

internal class TodoService(ITodoRepository todoRepository) : ITodoService
{
    public async Task<Todo> Add(Todo entity, CancellationToken cancellationToken = default)
    {
        await todoRepository.Add(entity, cancellationToken);
        return entity;
    }

    public async Task<Todo> Update(Todo entity, CancellationToken cancellationToken = default)
    {
        await todoRepository.Update(entity, cancellationToken);
        return entity;
    }

    public async Task Remove(Guid entityId, CancellationToken cancellationToken = default)
    {
        var entity = await todoRepository.Query().Where(x => x.Id == entityId).FirstOrDefault(cancellationToken)
            ?? throw new KeyNotFoundException($"Todo with ID {entityId} not found.");

        await todoRepository.Remove(entity, cancellationToken);
    }

    public Task<Todo?> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
        //return todoRepository.GetById(entityId, cancellationToken);
    }

    public async Task<List<Todo>> GetAll(CancellationToken cancellationToken = default)
    {
        return await todoRepository.Query().ToList(cancellationToken);
    }
}
