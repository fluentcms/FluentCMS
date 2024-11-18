namespace FluentCMS.Repositories.EFCore;

public class PermissionRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Permission>(dbContext, apiExecutionContext), IPermissionRepository
{
    public async Task<IEnumerable<Permission>> Set(Guid siteId, Guid entityId, string entityTypeName, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        var existPermissions = await DbContext.Permissions.Where(x => x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToListAsync(cancellationToken);

        await DeleteMany(existPermissions.Select(x => x.Id), cancellationToken);

        var permissions = roleIds.Select(x => new Permission
        {
            EntityType = entityTypeName,
            Action = action,
            EntityId = entityId,
            RoleId = x,
            SiteId = siteId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = ApiExecutionContext.Username
        });

        if (!permissions.Any())
            return [];

        return await CreateMany(permissions, cancellationToken);
    }

    public async Task<IEnumerable<Permission>> Get(Guid siteId, Guid entityId, string entityTypeName, string action, CancellationToken cancellationToken)
    {
        return await DbContext.Permissions.Where(x => x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToListAsync(cancellationToken);
    }
}
