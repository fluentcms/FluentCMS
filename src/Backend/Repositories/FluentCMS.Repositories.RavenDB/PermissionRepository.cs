namespace FluentCMS.Repositories.RavenDB;

public class PermissionRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Permission>(RavenDbContext, apiExecutionContext), IPermissionRepository
{
    public async Task<IEnumerable<Permission>> Set(Guid siteId, Guid entityId, string entityTypeName, string action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            // Delete existing permissions
            var existingPermissions = await session.Query<RavenEntity<Permission>>()
                                        .Where(x => x.Data.EntityId == entityId && x.Data.EntityType == entityTypeName && x.Data.Action == action)
                                        .ToListAsync(cancellationToken);

            foreach (var existingPermission in existingPermissions)
            {
                session.Delete(existingPermission);
            }

            // Create new permissions
            var permissions = roleIds.Select(x => new Permission
            {
                Id = Guid.NewGuid(),
                EntityType = entityTypeName,
                Action = action,
                EntityId = entityId,
                RoleId = x,
                SiteId = siteId
            });

            // Save the new permissions
            foreach (var permission in permissions)
            {
                SetAuditableFieldsForCreate(permission);

                await session.StoreAsync(new RavenEntity<Permission>(permission), cancellationToken);
            }

            await session.SaveChangesAsync(cancellationToken);

            return permissions;
        }
    }
}
