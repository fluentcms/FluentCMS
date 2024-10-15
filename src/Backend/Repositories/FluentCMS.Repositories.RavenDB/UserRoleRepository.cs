namespace FluentCMS.Repositories.RavenDB;

public class UserRoleRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<UserRole>(RavenDbContext, apiExecutionContext), IUserRoleRepository
{
    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<RavenEntity<UserRole>>()
                                    .Where(x => x.Data.SiteId == siteId && x.Data.UserId == userId)
                                    .Select(x => x.Data)
                                    .ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
    }

    public async Task<IEnumerable<UserRole>> GetByRoleId(Guid roleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<RavenEntity<UserRole>>()
                                    .Where(x => x.Data.RoleId == roleId)
                                    .Select(x => x.Data)
                                    .ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
    }

    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var qres = await session.Query<RavenEntity<UserRole>>()
                                    .Where(x => x.Data.UserId == userId)
                                    .Select(x => x.Data)
                                    .ToListAsync(cancellationToken);

            return qres.AsEnumerable();
        }
    }
}
