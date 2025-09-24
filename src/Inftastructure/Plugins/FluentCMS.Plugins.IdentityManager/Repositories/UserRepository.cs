namespace FluentCMS.Plugins.IdentityManager.Repositories;

public interface IUserRepository : IRepository<User>
{
}

public class UserRepository(ApplicationDbContext context, ILogger<UserRepository> logger) :
    Repository<User, ApplicationDbContext>(context, logger),
    IUserRepository
{
}