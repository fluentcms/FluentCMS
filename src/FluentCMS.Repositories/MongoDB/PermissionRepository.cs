using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Repository for managing Permission entities in MongoDB.
/// Extends SiteAssociatedRepository to handle permissions associated with specific sites.
/// </summary>
public class PermissionRepository : SiteAssociatedRepository<Permission>, IPermissionRepository
{
    /// <summary>
    /// Initializes a new instance of the PermissionRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context for the repository.</param>
    public PermissionRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

    /// <summary>
    /// Retrieves all Permission entities associated with a specific site and set of role IDs.
    /// </summary>
    /// <param name="siteId">The ID of the site for which to retrieve permissions.</param>
    /// <param name="roleIds">The collection of role IDs to filter permissions.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns a collection of permissions.</returns>
    public async Task<IEnumerable<Permission>> GetPermissions(Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Permission>.Filter.Eq(x => x.SiteId, siteId) &
                     Builders<Permission>.Filter.In(x => x.RoleId, roleIds);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.ToListAsync(cancellationToken);
    }
}
