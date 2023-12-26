namespace FluentCMS.Repositories.Decorators;

public abstract class EntityRepositoryDecorator<TEntity> : IEntityRepository<TEntity> where TEntity : IEntity
{
    public IAuthContext AuthContext { get; }
    private readonly IEntityRepository<TEntity> _decorator;

    public EntityRepositoryDecorator(IAuthContext authContext, IEntityRepository<TEntity> decorator)
    {
        AuthContext = authContext;
        _decorator = decorator;
    }

    public Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (typeof(TEntity) is IAuditableEntity)
        {
            var auditableEntity = entity as IAuditableEntity ??
                throw new AppException(ExceptionCodes.GeneralArgumentNullException);

            auditableEntity.CreatedBy = AuthContext.Username;
            auditableEntity.CreatedAt = DateTime.UtcNow;
        }

        return _decorator.Create(entity, cancellationToken);
    }

    public Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        if (typeof(TEntity) is IAuditableEntity)
        {
            foreach (var entity in entities)
            {
                var auditableEntity = entity as IAuditableEntity ??
                    throw new AppException(ExceptionCodes.GeneralArgumentNullException);

                auditableEntity.CreatedBy = AuthContext.Username;
                auditableEntity.CreatedAt = DateTime.UtcNow;
            }
        }
        return _decorator.CreateMany(entities, cancellationToken);
    }

    public Task<TEntity?> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return _decorator.Delete(id, cancellationToken);
    }

    public Task<IEnumerable<TEntity>> GetAll(CancellationToken cancellationToken = default)
    {
        return _decorator.GetAll(cancellationToken);
    }

    public Task<TEntity?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return _decorator.GetById(id, cancellationToken);
    }

    public Task<IEnumerable<TEntity>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        return _decorator.GetByIds(ids, cancellationToken);
    }

    public Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (typeof(TEntity) is IAuditableEntity)
        {
            var auditableEntity = entity as IAuditableEntity ??
                throw new AppException(ExceptionCodes.GeneralArgumentNullException);

            auditableEntity.ModifiedBy = AuthContext.Username;
            auditableEntity.ModifiedAt = DateTime.UtcNow;
        }

        return _decorator.Update(entity, cancellationToken);
    }
}
