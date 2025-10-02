namespace FluentCMS.Plugins.IdentityManager.Repositories;

public interface IUserRoleRepository : IRepository<UserRole>
{
}

internal class UserRoleRepository(ApplicationDbContext context, ILogger<UserRoleRepository> logger) :
    EfRepository<UserRole, ApplicationDbContext>(context, logger),
    IUserRoleRepository
{
}
