using FluentCMS.Entities;

namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    SiteAssociatedRepository<Role>(mongoDbContext, applicationContext),
    IRoleRepository
{
}
