using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Repository for managing Page entities in MongoDB.
/// Extends SiteAssociatedRepository for handling entities associated with a specific site.
/// </summary>
public class PageRepository : SiteAssociatedRepository<Page>, IPageRepository
{
    /// <summary>
    /// Initializes a new instance of the PageRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context for the repository.</param>
    public PageRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

    /// <summary>
    /// Retrieves a Page entity by its path and associated site ID.
    /// </summary>
    /// <param name="siteId">The ID of the site associated with the page.</param>
    /// <param name="path">The path of the page to retrieve.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the Page entity if found.</returns>
    public async Task<Page> GetByPath(Guid siteId, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Page>.Filter.Eq(x => x.Path, path) &
                     Builders<Page>.Filter.Eq(x => x.SiteId, siteId);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
