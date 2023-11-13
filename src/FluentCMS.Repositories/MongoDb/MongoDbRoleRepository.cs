using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbRoleRepository : MongoDbGenericRepository<Role>, IRoleRepository
{
    // TODO: add index
    public MongoDbRoleRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }

    public IQueryable<Role> AsQueryable()
    {
        return Collection.AsQueryable();
    }

    public async Task<Role?> FindByName(string normalizedRoleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Role>.Filter.Eq(x => x.NormalizedName, normalizedRoleName);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
