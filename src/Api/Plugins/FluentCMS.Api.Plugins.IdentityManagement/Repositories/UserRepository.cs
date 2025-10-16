namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

public interface IUserRepository : IRepository<User>
{
}

internal class UserRepository(AppIdentityDbContext dbContext) : Repository<User, AppIdentityDbContext>(dbContext), IUserRepository;
