namespace FluentCMS.Repositories.MongoDB;

public class UserRoleRepository : SiteAssociatedRepository<UserRole>, IUserRoleRepository
{
    public UserRoleRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.SiteId == siteId && x.UserId == userId).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.UserId == userId).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<UserRole>> GetByRoleId(Guid roleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.RoleId == roleId).ToListAsync(cancellationToken);
    }
}
