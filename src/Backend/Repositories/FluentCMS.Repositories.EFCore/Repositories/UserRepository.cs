using AutoMapper.QueryableExtensions;
using System.Security.Claims;

namespace FluentCMS.Repositories.EFCore;

public class UserRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<User, UserModel>(dbContext, mapper, apiExecutionContext), IUserRepository
{
    public IQueryable<User> AsQueryable()
    {
        return DbContext.Users.ProjectTo<User>(Mapper.ConfigurationProvider); // AutoMapper projection
    }

    public async Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken = default)
    {
        var dbEntities = await DbContext.Users.Where(u => u.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value)).ToListAsync(cancellationToken);
        return Mapper.Map<IList<User>>(dbEntities);
    }

    public async Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        var dbEntity = await DbContext.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        return Mapper.Map<User?>(dbEntity);
    }

    public async Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
    {
        var dbEntity = await DbContext.Users.FirstOrDefaultAsync(user => user.Logins.Any(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey), cancellationToken);
        return Mapper.Map<User?>(dbEntity);
    }

    public async Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        var dbEntity = await DbContext.Users.FirstOrDefaultAsync(x => x.NormalizedUserName == normalizedUserName, cancellationToken);
        return Mapper.Map<User?>(dbEntity);
    }
}
