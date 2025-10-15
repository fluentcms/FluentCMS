//namespace FluentCMS.Api.Core.Repositories.EntityFramework;

//public class UserRoleRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<UserRole>(dbContext), IUserRoleRepository
//{
//    public async Task<IEnumerable<UserRole>> GetUserRoles(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
//    {
//        return await Query().Where(x => x.SiteId == siteId && x.UserId == userId).ToList(cancellationToken);
//    }

//    public async Task<IEnumerable<UserRole>> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
//    {
//        return await Query().Where(x => x.UserId == userId).ToList(cancellationToken);
//    }
//}
