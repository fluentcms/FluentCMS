namespace FluentCMS.Repositories.LiteDb;

public class AuditableEntityRepository<TEntity> : EntityRepository<TEntity>, IAuditableEntityRepository<TEntity> where TEntity : IAuditableEntity
{
    protected readonly IApiExecutionContext ApiExecutionContext;

    public AuditableEntityRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : base(liteDbContext)
    {
        ApiExecutionContext = apiExecutionContext;
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
        entity.CreatedBy = ApiExecutionContext.Username;
    }

    private void SetAuditableFieldsForUpdate(TEntity entity)
    {
        entity.ModifiedAt = DateTime.UtcNow;
        entity.ModifiedBy = ApiExecutionContext.Username;
    }
}
