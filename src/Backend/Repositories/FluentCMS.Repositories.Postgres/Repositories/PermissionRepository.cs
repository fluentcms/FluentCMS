namespace FluentCMS.Repositories.Postgres.Repositories;

public class PermissionRepository(PostgresDbContext context) : SiteAssociatedRepository<Permission>(context), IPermissionRepository, IService
{
    public async Task<IEnumerable<Permission>> Set(Guid siteId, Guid entityId, string entityTypeName, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existPermissions = await Table.Where(x => x.EntityId == entityId && x.EntityType == entityTypeName && x.Action == action).ToListAsync(cancellationToken: cancellationToken);

        await DeleteMany(existPermissions.Select(x => x.Id), cancellationToken);

        var permissions = roleIds.Select(x => new Permission
        {
            EntityType = entityTypeName,
            Action = action,
            EntityId = entityId,
            RoleId = x,
            SiteId = siteId
        }).ToList();

        if (permissions.Count == 0)
            return [];

        return await CreateMany(permissions, cancellationToken);
    }
}
