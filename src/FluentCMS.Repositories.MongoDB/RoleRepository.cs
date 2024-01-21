namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository : AppAssociatedRepository<Role>, IRoleRepository
{
    public RoleRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

}
