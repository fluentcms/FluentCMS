namespace FluentCMS.Repositories.RavenDB;

public class RoleRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Role>(RavenDbContext, apiExecutionContext), IRoleRepository
{
}
