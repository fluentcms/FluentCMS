namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

public interface IRoleRepository : IRepository<Role>
{
}

internal class RoleRepository(AppIdentityDbContext dbContext) : Repository<Role, AppIdentityDbContext>(dbContext), IRoleRepository;
