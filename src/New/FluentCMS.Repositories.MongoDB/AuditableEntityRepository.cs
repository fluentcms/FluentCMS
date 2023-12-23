using FluentCMS.Entities;

namespace FluentCMS.Repositories.MongoDB;

public abstract class AuditableEntityRepository<TEntity> : EntityRepository<TEntity>, IAuditableEntityRepository<TEntity>
    where TEntity : IAuditableEntity
{
    protected readonly IAuthContext AuthContext;

    public AuditableEntityRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
        AuthContext = authContext;
    }

    public override async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditableFieldsForCreate(entity);
        return await base.Create(entity, cancellationToken);
    }

    public override async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var entity in entities)
            SetAuditableFieldsForCreate(entity);

        return await base.CreateMany(entities, cancellationToken);
    }

    public override async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditableFieldsForUpdate(entity);
        return await base.Update(entity, cancellationToken);
    }

    private void SetAuditableFieldsForCreate(TEntity entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.CreatedBy = AuthContext.Username;
    }

    private void SetAuditableFieldsForUpdate(TEntity entity)
    {
        entity.ModifiedAt = DateTime.UtcNow;
        entity.ModifiedBy = AuthContext.Username;
    }
}
