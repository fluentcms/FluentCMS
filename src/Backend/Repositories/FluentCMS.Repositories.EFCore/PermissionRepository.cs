﻿namespace FluentCMS.Repositories.EFCore;

public class PermissionRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Permission>(dbContext, apiExecutionContext), IPermissionRepository
{
    public async Task<IEnumerable<Permission>> Set(Guid siteId, Guid entityId, string entityTypeName, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        var existPermissions = await DbSet.Where(x => x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToListAsync(cancellationToken);

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

    public async Task<IEnumerable<Permission>> Get(Guid siteId, Guid entityId, string entityTypeName, string action, CancellationToken cancellationToken)
    {
        return await DbSet.Where(x => x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToListAsync(cancellationToken);
    }
}
