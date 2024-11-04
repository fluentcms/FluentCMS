namespace FluentCMS.Repositories.RavenDB;

public abstract class AuditableEntityRepository<TEntity>(IRavenDBContext dbContext, IApiExecutionContext apiExecutionContext) : EntityRepository<TEntity>(dbContext), IAuditableEntityRepository<TEntity> where TEntity : IAuditableEntity
{
    protected readonly IApiExecutionContext ApiExecutionContext = apiExecutionContext;

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

        using (var session = Store.OpenAsyncSession())
        {
            var id = entity.Id;
            var dbEntity = await session.Query<RavenEntity<TEntity>>().SingleOrDefaultAsync(x => x.Data.Id == id, cancellationToken);
            if (dbEntity == null)
            {
                SetAuditableFieldsForCreate(entity);

                dbEntity = new RavenEntity<TEntity>(entity);

                await session.StoreAsync(dbEntity, cancellationToken);
            }
            else
            {
                entity.CopyProperties(dbEntity.Data);

                SetAuditableFieldsForUpdate(dbEntity.Data, dbEntity.Data);
            }

            await session.SaveChangesAsync(cancellationToken);

            return dbEntity.Data;
        }
    }

    protected void SetAuditableFieldsForCreate(TEntity entity)
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        entity.CreatedAt = DateTime.UtcNow;
        entity.CreatedBy = ApiExecutionContext.Username;
    }

    protected void SetAuditableFieldsForUpdate(TEntity entity, TEntity oldEntity)
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        entity.CreatedAt = oldEntity.CreatedAt;
        entity.CreatedBy = oldEntity.CreatedBy;
        entity.ModifiedAt = DateTime.UtcNow;
        entity.ModifiedBy = ApiExecutionContext.Username;
    }
}
