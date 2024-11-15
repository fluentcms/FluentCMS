namespace FluentCMS.Repositories.EFCore;

public class RoleRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Role>(dbContext, apiExecutionContext), IRoleRepository
{
}
