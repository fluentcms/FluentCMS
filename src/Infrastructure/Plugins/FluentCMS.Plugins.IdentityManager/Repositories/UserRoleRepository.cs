namespace FluentCMS.Plugins.IdentityManager.Repositories;

public interface IUserRoleRepository : IRepository<UserRole>
{
}

public class UserRoleRepository(ApplicationDbContext context, ILogger<UserRoleRepository> logger) :
    Repository<UserRole, ApplicationDbContext>(context, logger),
    IUserRoleRepository
{
}