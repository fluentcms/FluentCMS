namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository : AppAssociatedRepository<Role>, IRoleRepository
{
    public RoleRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }

}
