namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository : AuditableEntityRepository<Role>, IRoleRepository
{
    public RoleRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

}
