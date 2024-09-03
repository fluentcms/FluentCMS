namespace FluentCMS.Repositories.MongoDB;

public class PermissionRepository : SiteAssociatedRepository<Permission>, IPermissionRepository
{
    public PermissionRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : base(mongoDbContext, apiExecutionContext)
    {
    }

    public async Task<IEnumerable<Permission>> GetByEntityId(Guid entityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.EntityId == entityId).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Permission>> GetByActionAndEntityId(string action, Guid entityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.Action == action && x.EntityId == entityId).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Permission>> SetPermissions<TEntity>(TEntity entity, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default) where TEntity : ISiteAssociatedEntity
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existPermissions = await Collection.Find(x => x.EntityId == entity.Id && x.Action == action).ToListAsync();
        await DeleteMany(existPermissions.Select(x => x.Id), cancellationToken);

        var permissions = roleIds.Select(x => new Permission
        {
            EntityType = entity.GetType().Name,
            Action = action,
            EntityId = entity.Id,
            RoleId = x,
            SiteId = entity.SiteId
        });

        return await CreateMany(permissions, cancellationToken);
    }
}
