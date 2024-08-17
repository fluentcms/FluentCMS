using FluentCMS.Entities.Sites;

namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository : SiteAssociatedRepository<Role>, IRoleRepository
{
    public RoleRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

}
