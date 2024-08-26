namespace FluentCMS.Repositories.LiteDb;

public class PermissionRepository : SiteAssociatedRepository<Permission>, IPermissionRepository
{
    public PermissionRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<Permission>> GetByEntityId(Guid entityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.EntityId == entityId).ToListAsync();
    }

    public async Task<IEnumerable<Permission>> GetByActionAndEntityId(string action, Guid entityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.Action == action && x.EntityId == entityId).ToListAsync();
    }

    public async Task<IEnumerable<Permission>> SetPermissions<TEntity>(TEntity entity, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default) where TEntity : ISiteAssociatedEntity
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existPermissions = await Collection.Query().Where(x => x.EntityId == entity.Id).ToListAsync();
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
