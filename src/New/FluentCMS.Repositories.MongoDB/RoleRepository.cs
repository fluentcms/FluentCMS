using FluentCMS.Entities;

namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository : AppAssociatedRepository<Role>, IRoleRepository
{
    public RoleRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext) : base(mongoDbContext, applicationContext)
    {
    }

}
