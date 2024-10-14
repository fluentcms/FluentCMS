namespace FluentCMS.Repositories.Postgres.Repositories;

public class UserRoleRepository(PostgresDbContext context) : SiteAssociatedRepository<UserRole>(context), IUserRoleRepository, IService
{
    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await GetAllByExpression(x => x.SiteId == siteId && x.UserId == userId, cancellationToken);
    }

    public async Task<IEnumerable<UserRole>> GetByRoleId(Guid roleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await GetAllByExpression(x => x.RoleId == roleId, cancellationToken);
    }

    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await GetAllByExpression(x => x.UserId == userId, cancellationToken);
    }
}
