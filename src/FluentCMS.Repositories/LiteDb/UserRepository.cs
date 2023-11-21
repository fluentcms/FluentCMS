using FluentCMS.Entities;
using LiteDB.Queryable;
using System.Security.Claims;

namespace FluentCMS.Repositories.LiteDb;

public class UserRepository : GenericRepository<User>, IUserRepository
{
    public UserRepository(LiteDbContext dbContext, IApplicationContext applicationContext) : base(dbContext, applicationContext)
    {
    }

    public IQueryable<User> AsQueryable()
    {
        return Collection.AsQueryable();
    }

    public async Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.FindOneAsync(x => x.NormalizedEmail == normalizedEmail);
    }

    public Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = Collection.AsQueryable().FirstOrDefault(x =>
            x.Logins.Any(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey));
        return Task.FromResult(data);
    }

    public async Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.FindOneAsync(x => x.NormalizedUserName == normalizedUserName);
    }

    public async Task<User?> GetByUsername(string username, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.FindOneAsync(x => x.UserName == username);
    }

    public async Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = await Collection.FindAsync(x => x.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value));
        return data.ToList();
    }

    public async Task<IList<User>> GetUsersInRole(string roleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = await Collection.FindAsync(x => x.RoleIds.Any(r => roleId.Equals(r)));
        return data.ToList();
    }
}
