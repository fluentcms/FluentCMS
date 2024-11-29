namespace FluentCMS.Repositories.Caching;

public class UserRoleRepository(IUserRoleRepository repository, ICacheProvider cacheProvider) : SiteAssociatedRepository<UserRole>(repository, cacheProvider), IUserRoleRepository
{
    public async Task<IEnumerable<UserRole>> GetByRoleId(Guid roleId, CancellationToken cancellationToken = default)
    {
        var entities = await GetAll(cancellationToken);
        return entities.Where(x => x.RoleId == roleId);
    }

    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        var entities = await GetAll(cancellationToken);
        return entities.Where(x => x.UserId == userId);
    }

    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        var entities = await GetAll(cancellationToken);
        return entities.Where(x => x.UserId == userId && x.SiteId == siteId);
    }
}
