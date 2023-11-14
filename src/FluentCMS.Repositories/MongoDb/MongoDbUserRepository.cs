using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;
using System.Security.Claims;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbUserRepository : MongoDbGenericRepository<User>, IUserRepository
{
    // TODO: add index
    public MongoDbUserRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }

    public IQueryable<User> AsQueryable()
    {
        return Collection.AsQueryable();
    }

    public Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var users = Collection.AsQueryable().Where(u => u.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value)).ToList();

        return Task.FromResult((IList<User>)users);
    }

    public Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = Collection.AsQueryable().Where(x => x.NormalizedEmail == normalizedEmail).SingleOrDefault();

        return Task.FromResult(user);
    }

    public Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = Collection.AsQueryable().Where(user => user.Logins.Any(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey)).FirstOrDefault();

        return Task.FromResult(user);
    }

    public Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = Collection.AsQueryable().Where(x => x.NormalizedUserName == normalizedUserName).FirstOrDefault();

        return Task.FromResult(user);
    }

    public Task<IList<User>> GetUsersInRole(string roleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = Collection.AsQueryable().Where(x => x.RoleIds.Any(r => roleId.Equals(r))).ToList();

        return Task.FromResult((IList<User>)user);
    }
}
