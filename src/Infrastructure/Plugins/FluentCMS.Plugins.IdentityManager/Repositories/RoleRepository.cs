namespace FluentCMS.Plugins.IdentityManager.Repositories;

public interface IRoleRepository : IRepository<Role>
{
}

public class RoleRepository(ApplicationDbContext context, ILogger<RoleRepository> logger) :
    Repository<Role, ApplicationDbContext>(context, logger),
    IRoleRepository
{
}

