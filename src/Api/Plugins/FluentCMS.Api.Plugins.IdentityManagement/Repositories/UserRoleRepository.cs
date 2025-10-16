namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

public interface IUserRoleRepository : ISiteAssociatedRepository<UserRole>
{
    Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default);
}

internal class UserRoleRepository(AppIdentityDbContext dbContext) : SiteAssociatedRepository<UserRole, AppIdentityDbContext>(dbContext), IUserRoleRepository
{
    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        return await Query().Where(x => x.SiteId == siteId && x.UserId == userId).ToList(cancellationToken);
    }
}
