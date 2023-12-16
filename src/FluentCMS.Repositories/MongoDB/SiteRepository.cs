using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Repository for managing Site entities in MongoDB.
/// Extends SiteAssociatedRepository for handling entities associated with a specific site.
/// </summary>
public class SiteRepository : SiteAssociatedRepository<Site>, ISiteRepository
{
    /// <summary>
    /// Initializes a new instance of the SiteRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context for the repository.</param>
    public SiteRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

    /// <summary>
    /// Creates a new Site entity in the database. After creation, the SiteId is updated to match the Id.
    /// </summary>
    /// <param name="entity">The Site entity to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the created Site entity.</returns>
    public override async Task<Site?> Create(Site entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var newSite = await base.Create(entity, cancellationToken);

        if (newSite == null)
            return null;

        newSite.SiteId = newSite.Id;

        return await Update(newSite, cancellationToken);
    }

    /// <summary>
    /// Retrieves a Site entity by its URL.
    /// </summary>
    /// <param name="url">The URL of the site.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the Site entity, if found.</returns>
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Site>.Filter.AnyEq(x => x.Urls, url);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
