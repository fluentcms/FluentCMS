using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class PermissionRepository : GenericRepository<Permission>, IPermissionRepository
{
    public PermissionRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext) : base(mongoDbContext, applicationContext)
    {
    }

    public async Task<IEnumerable<Permission>> GetPermissions(Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Permission>.Filter.Eq(x => x.SiteId, siteId);
        filter &= Builders<Permission>.Filter.In(x => x.RoleId, roleIds);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.ToEnumerable(cancellationToken);
    }
}
