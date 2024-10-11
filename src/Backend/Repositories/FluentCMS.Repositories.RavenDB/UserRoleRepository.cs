namespace FluentCMS.Repositories.RavenDB;

public class UserRoleRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<UserRole>(RavenDbContext, apiExecutionContext), IUserRoleRepository
{
    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<UserRole>().Where(x => x.SiteId == siteId && x.UserId == userId).ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
    }

    public async Task<IEnumerable<UserRole>> GetByRoleId(Guid roleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<UserRole>().Where(x => x.RoleId == roleId).ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
    }

    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<UserRole>().Where(x => x.UserId == userId).ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
    }
}
