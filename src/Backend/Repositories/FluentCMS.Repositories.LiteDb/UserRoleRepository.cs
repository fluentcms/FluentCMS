namespace FluentCMS.Repositories.LiteDb;

public class UserRoleRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<UserRole>(liteDbContext, apiExecutionContext), IUserRoleRepository
{
    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.SiteId == siteId && x.UserId == userId).ToListAsync();
    }

    public async Task<IEnumerable<UserRole>> GetByRoleId(Guid roleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.RoleId == roleId).ToListAsync();
    }

    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.UserId == userId).ToListAsync();
    }
}
