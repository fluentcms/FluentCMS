using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class RoleRepository : GenericRepository<Role>, IRoleRepository
{
    public RoleRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext) : base(mongoDbContext, applicationContext)
    {
    }

    public async Task<IEnumerable<Role>> GetAll(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var siteIdFilter = Builders<Role>.Filter.Eq(x => x.SiteId, siteId);

        var roles = await Collection.FindAsync(siteIdFilter, null, cancellationToken);

        return roles.ToEnumerable(cancellationToken);
    }
}
