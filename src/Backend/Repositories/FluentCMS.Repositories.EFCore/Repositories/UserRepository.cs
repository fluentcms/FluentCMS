//using System.Security.Claims;

//namespace FluentCMS.Repositories.EFCore;

//public class UserRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<User>(dbContext, apiExecutionContext), IUserRepository
//{
//    public IQueryable<User> AsQueryable()
//    {
//        return DbContext.Users.AsQueryable();
//    }

//    public async Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken = default)
//    {
//        return await DbContext.Users.Where(u => u.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value)).ToListAsync(cancellationToken);
//    }

//    public async Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken = default)
//    {
//        return await DbContext.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
//    }

//    public async Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
//    {
//        return await DbContext.Users.FirstOrDefaultAsync(user => user.Logins.Any(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey), cancellationToken);
//    }

//    public async Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken = default)
//    {
//        return await DbContext.Users.FirstOrDefaultAsync(x => x.NormalizedUserName == normalizedUserName, cancellationToken);
//    }
//}
