namespace FluentCMS.Repositories.LiteDb;

public class PermissionRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Permission>(liteDbContext, apiExecutionContext), IPermissionRepository
{
    public async Task<IEnumerable<Permission>> Set(Guid siteId, Guid entityId, string entityTypeName, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existPermissions = await Collection.Query().Where(x => x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToListAsync();

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
}
