namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

public interface IUserRepository : IRepository<User>
{
}

internal class UserRepository(ApplicationDbContext context) : Repository<User, ApplicationDbContext>(context), IUserRepository
{
}
