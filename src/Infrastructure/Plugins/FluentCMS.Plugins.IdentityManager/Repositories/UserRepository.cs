namespace FluentCMS.Plugins.IdentityManager.Repositories;

public interface IUserRepository : IRepository<User>
{
}

internal class UserRepository(ApplicationDbContext context, ILogger<UserRepository> logger) :
    EfRepository<User, ApplicationDbContext>(context, logger),
    IUserRepository
{
}
