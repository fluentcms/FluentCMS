namespace FluentCMS.Repositories.LiteDb;

public class UserRoleRepository : SiteAssociatedRepository<UserRole>, IUserRoleRepository
{
    public UserRoleRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<UserRole>> GetBySiteAndUserId(Guid siteId, Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.SiteId == siteId && x.UserId == userId).ToListAsync();
    }

    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.UserId == userId).ToListAsync();
    }
}
