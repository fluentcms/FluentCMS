using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Application.Services;

// TODO: better exception handling

public class GenericService<TEntity> : IGenericService<TEntity> where TEntity : class, IEntity
{
    protected readonly IGenericRepository<TEntity> Repository;

    public GenericService(IGenericRepository<TEntity> repository)
    {
        Repository = repository;
    }

    public virtual async Task<TEntity> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await Repository.GetById(id, cancellationToken);
        if (entity == null)
            throw new Exception(id.ToString());

        return entity;
    }

    public virtual async Task<TEntity> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        var created = await Repository.Create(entity, cancellationToken);

        if (created == null)
            throw new Exception(entity.Id.ToString());

        return created;
    }

    public virtual async Task<TEntity> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        var update = await Repository.Update(entity);

        if (update == null)
            throw new Exception(entity.Id.ToString());

        return update;
    }

    public virtual async Task<TEntity> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var delete = await Repository.Delete(id, cancellationToken);

        if (delete == null)
            throw new Exception(id.ToString());

        return delete;
    }

    public virtual async Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        return await Repository.GetAll(cancellationToken);
    }

}

