namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

public interface IRoleRepository : ISiteAssociatedRepository<Role>
{
}

internal class RoleRepository(AppIdentityDbContext dbContext) : SiteAssociatedRepository<Role, AppIdentityDbContext>(dbContext), IRoleRepository;
