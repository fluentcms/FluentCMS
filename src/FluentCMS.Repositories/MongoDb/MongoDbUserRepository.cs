using FluentCMS.Entities.Users;
using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbUserRepository(IMongoDBContext mongoDbContext) : MongoDbGenericRepository<User>(mongoDbContext), IUserRepository
{
    public async Task<User?> GetByUsername(string username, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<User>.Filter.Eq(x => x.Username, username);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
