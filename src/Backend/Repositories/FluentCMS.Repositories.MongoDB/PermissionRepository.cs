namespace FluentCMS.Repositories.MongoDB;

public class PermissionRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Permission>(mongoDbContext, apiExecutionContext), IPermissionRepository
{
    public async Task<IEnumerable<Permission>> Set(Guid siteId, Guid entityId, string entityTypeName, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existPermissions = await Collection.Find(x => x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToListAsync(cancellationToken: cancellationToken);

        await DeleteMany(existPermissions.Select(x => x.Id), cancellationToken);

        var permissions = roleIds.Select(x => new Permission
        {
            EntityType = entityTypeName,
            Action = action,
            EntityId = entityId,
            RoleId = x,
            SiteId = siteId
        });

        if (!permissions.Any())
            return [];

        return await CreateMany(permissions, cancellationToken);
    }

    public async Task<IEnumerable<Permission>> Get(Guid siteId, Guid entityId, string entityTypeName, string action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existPermissions = await Collection.Find(x => x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToListAsync(cancellationToken: cancellationToken);

        return existPermissions;
    }
}
