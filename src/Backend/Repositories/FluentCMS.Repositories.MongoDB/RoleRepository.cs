namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Role>(mongoDbContext, apiExecutionContext), IRoleRepository
{
}
