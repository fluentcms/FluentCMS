using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Represents a MongoDB-based repository for managing plugin content entities in the FluentCMS system.
/// This class provides specific data access functionalities for plugin content.
/// </summary>
/// <remarks>
/// This repository extends the generic ContentRepository for PluginContent type
/// and implements the IPluginContentRepository interface for specialized operations.
/// </remarks>
public class PluginContentRepository : ContentRepository<PluginContent>, IPluginContentRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginContentRepository"/> class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used for data operations.</param>
    /// <param name="applicationContext">The application context that provides access to shared resources and functionality.</param>
    public PluginContentRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

    /// <summary>
    /// Asynchronously retrieves a collection of PluginContent entities associated with a specific plugin ID.
    /// </summary>
    /// <param name="contentType">The content type to filter the plugin content.</param>
    /// <param name="pluginId">The plugin ID for which content is to be retrieved.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation, containing a collection of PluginContent entities.</returns>
    public async Task<IEnumerable<PluginContent>> GetByPluginId(string contentType, Guid pluginId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var builder = Builders<PluginContent>.Filter;
        var filter = builder.Eq(x => x.PluginId, pluginId);
        var result = await Collection.FindAsync(filter, null, cancellationToken);
        return result.ToEnumerable(cancellationToken);
    }
}
