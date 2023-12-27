using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Represents a MongoDB-based repository for managing content entities in the FluentCMS system.
/// This class provides MongoDB specific implementation for content entity operations.
/// </summary>
/// <typeparam name="TContent">The type of content entity this repository manages.
/// TContent must be derived from the Content class and must have a parameterless constructor.</typeparam>
/// <remarks>
/// This repository implementation uses MongoDB for data storage and retrieval,
/// and it inherits from SiteAssociatedRepository to provide common functionality
/// associated with site-related entities.
/// </remarks>
public class ContentRepository<TContent> :
    SiteAssociatedRepository<TContent>,
    IContentRepository<TContent>
    where TContent : Content, new()
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContentRepository{TContent}"/> class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context to be used for data operations.</param>
    /// <param name="applicationContext">The application context that provides access to shared resources and functionality.</param>
    public ContentRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TContent>> GetAllForSite(string contentType, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<TContent>.Filter.Eq(x => x.Type, contentType.ToLowerInvariant());
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }

    public override Task<TContent?> Create(TContent entity, CancellationToken cancellationToken = default)
    {
        entity.Type = entity.Type.ToLowerInvariant();
        return base.Create(entity, cancellationToken);
    }

    public override Task<TContent?> Update(TContent entity, CancellationToken cancellationToken = default)
    {
        entity.Type = entity.Type.ToLowerInvariant();
        return base.Update(entity, cancellationToken);
    }
}
