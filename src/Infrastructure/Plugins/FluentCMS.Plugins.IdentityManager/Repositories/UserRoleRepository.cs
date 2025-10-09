namespace FluentCMS.Plugins.IdentityManager.Repositories;

public interface IUserRoleRepository : IRepository<UserRole>
{
}

internal class UserRoleRepository(ApplicationDbContext context) : Repository<UserRole, ApplicationDbContext>(context), IUserRoleRepository
{
}
