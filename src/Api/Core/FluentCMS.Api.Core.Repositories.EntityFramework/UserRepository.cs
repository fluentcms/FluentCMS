namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class UserRepository(CmsCoreDbContext dbContext) : RepositoryBase<User>(dbContext), IUserRepository;
