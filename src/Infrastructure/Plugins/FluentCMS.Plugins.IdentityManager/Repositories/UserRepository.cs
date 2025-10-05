using FluentCMS.Repositories.EntityFramework;

namespace FluentCMS.Plugins.IdentityManager.Repositories;

public interface IUserRepository : IRepository<User>
{
}

internal class UserRepository(ApplicationDbContext context) : Repository<User, ApplicationDbContext>(context), IUserRepository
{
}
