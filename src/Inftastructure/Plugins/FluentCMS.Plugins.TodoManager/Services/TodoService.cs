using FluentCMS.Plugins.TodoManagement.Models;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Plugins.TodoManager.Services;

public interface ITodoService
{
    Task<Todo> Add(Todo entity, CancellationToken cancellationToken = default);
    Task<IEnumerable<Todo>> GetAll(CancellationToken cancellationToken = default);
    Task<Todo?> GetById(Guid entityId, CancellationToken cancellationToken = default);
    Task Remove(Guid entityId, CancellationToken cancellationToken = default);
    Task<Todo> Update(Todo entity, CancellationToken cancellationToken = default);
}

public class TodoService(ICachedRepository<Todo> todoRepository) : ITodoService
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
        await todoRepository.Remove(entityId, cancellationToken);
    }

    public Task<Todo?> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        return todoRepository.GetById(entityId, cancellationToken);
    }

    public Task<IEnumerable<Todo>> GetAll(CancellationToken cancellationToken = default)
    {
        return todoRepository.GetAll(cancellationToken);
    }
}
