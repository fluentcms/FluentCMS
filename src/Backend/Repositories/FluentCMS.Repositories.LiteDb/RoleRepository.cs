using FluentCMS.Entities.Sites;

namespace FluentCMS.Repositories.LiteDb;

public class RoleRepository : SiteAssociatedRepository<Role>, IRoleRepository
{
    public RoleRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

}
