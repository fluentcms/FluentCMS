using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using System.Security.Claims;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbUserRepository : LiteDbGenericRepository<User>, IUserRepository
{
    public LiteDbUserRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public IQueryable<User> AsQueryable()
    {
        throw new NotImplementedException();
    }

    public Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task<User?> GetByUsername(string username, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = await Collection.FindOneAsync(x => x.UserName == username);
        return data;
    }

    public Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<IList<User>> GetUsersInRole(string roleId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
