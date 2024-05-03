using System.Security.Claims;
using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using LiteDB.Queryable;

namespace FluentCMS.Repositories.LiteDb;

public class UserRepository(
    ILiteDBContext liteDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<User>(liteDbContext, authContext),
    IUserRepository
{
    public IQueryable<User> AsQueryable()
    {
        return Collection.AsQueryable();
    }

    public Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var users = AsQueryable().Where(u => u.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value)).ToList();
        return Task.FromResult((IList<User>)users);
    }

    public Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = AsQueryable().SingleOrDefault(x => x.NormalizedEmail == normalizedEmail);
        return Task.FromResult(user);
    }

    public Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = AsQueryable().FirstOrDefault(user => user.Logins.Any(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey));
        return Task.FromResult(user);
    }

    public Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = AsQueryable().FirstOrDefault(x => x.NormalizedUserName == normalizedUserName);
        return Task.FromResult(user);
    }

    public Task<IList<User>> GetUsersInRole(string roleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var users = Collection.AsQueryable().Where(x => x.RoleIds.Any(r => roleId.Equals(r))).ToList();
        return Task.FromResult((IList<User>)users);
    }
}
