namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

public interface IUserRoleRepository : IRepository<UserRole>
{
    Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, CancellationToken cancellationToken = default);
}

internal class UserRoleRepository(AppIdentityDbContext dbContext) : Repository<UserRole, AppIdentityDbContext>(dbContext), IUserRoleRepository
{
    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, CancellationToken cancellationToken = default)
    {
        return await Query().Where(x => x.UserId == userId).ToList(cancellationToken);
    }
}
