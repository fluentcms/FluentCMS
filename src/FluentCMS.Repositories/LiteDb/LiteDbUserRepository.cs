using FluentCMS.Entities.Users;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbUserRepository : LiteDbGenericRepository<User>, IUserRepository
{
    public LiteDbUserRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<User?> GetByUsername(string username, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = await Collection.FindOneAsync(x => x.Username == username);
        return data;
    }
}
