namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

public interface IRoleRepository : IRepository<Role>
{
}

internal class RoleRepository(ApplicationDbContext context) : Repository<Role, ApplicationDbContext>(context), IRoleRepository
{
}

