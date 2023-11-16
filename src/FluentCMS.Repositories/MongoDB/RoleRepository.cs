using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository : GenericRepository<Role>, IRoleRepository
{
    public RoleRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
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
