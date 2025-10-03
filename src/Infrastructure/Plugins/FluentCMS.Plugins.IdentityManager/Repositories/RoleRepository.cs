namespace FluentCMS.Plugins.IdentityManager.Repositories;

public interface IRoleRepository : IRepository<Role>
{
}

internal class RoleRepository(ApplicationDbContext context, ILogger<RoleRepository> logger) :
    EfRepository<Role, ApplicationDbContext>(context, logger),
    IRoleRepository
{
}

