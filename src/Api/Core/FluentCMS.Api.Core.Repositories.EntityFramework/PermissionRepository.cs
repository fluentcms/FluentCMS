namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class PermissionRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<Permission>(dbContext), IPermissionRepository
{
    public async Task<IEnumerable<Permission>> Set(Guid siteId, Guid entityId, string entityTypeName, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        var existingPermissions = await Query().Where(x => x.SiteId == siteId && x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToList(cancellationToken);
        var toRemove = existingPermissions.Where(x => !roleIds.Contains(x.RoleId)).ToList();
        var toAdd = roleIds.Where(roleId => !existingPermissions.Any(p => p.RoleId == roleId)).Select(roleId => new Permission
        {
            SiteId = siteId,
            EntityId = entityId,
            EntityType = entityTypeName,
            Action = action,
            RoleId = roleId
        }).ToList();
        if (toRemove.Any())
        {
            await RemoveRange(toRemove, cancellationToken);
        }
        if (toAdd.Count != 0)
        {
            await AddRange(toAdd, cancellationToken);
        }
        return await Query().Where(x => x.SiteId == siteId && x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToList(cancellationToken);

    }

    public async Task<IEnumerable<Permission>> Get(Guid siteId, Guid entityId, string entityTypeName, string action, CancellationToken cancellationToken)
    {
        return await Query().Where(x => x.SiteId == siteId && x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToList(cancellationToken);
    }
}
