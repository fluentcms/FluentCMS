using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Repository for managing Plugin entities in MongoDB.
/// Extends SiteAssociatedRepository to handle plugins associated with specific sites and pages.
/// </summary>
public class PluginRepository : SiteAssociatedRepository<Plugin>, IPluginRepository
{
    /// <summary>
    /// Initializes a new instance of the PluginRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context for the repository.</param>
    public PluginRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

    /// <summary>
    /// Retrieves all Plugin entities associated with a specific page ID.
    /// </summary>
    /// <param name="pageId">The ID of the page for which to retrieve plugins.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns a collection of plugins.</returns>
    public async Task<IEnumerable<Plugin>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<Plugin>.Filter.Eq(x => x.PageId, pageId);
        var result = await Collection.FindAsync(filter, cancellationToken: cancellationToken);
        return await result.ToListAsync(cancellationToken);
    }
}
