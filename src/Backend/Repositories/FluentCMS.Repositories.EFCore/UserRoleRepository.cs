namespace FluentCMS.Repositories.EFCore;

public class UserRoleRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<UserRole>(dbContext, apiExecutionContext), IUserRoleRepository
{
    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(x => x.SiteId == siteId && x.UserId == userId).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<UserRole>> GetByRoleId(Guid roleId, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(x => x.RoleId == roleId).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
    }
}
