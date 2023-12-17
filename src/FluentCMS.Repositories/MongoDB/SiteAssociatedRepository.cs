using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Extends the AuditEntityRepository to handle entities associated with a specific site.
/// This repository is designed for multi-tenant scenarios, where entities are scoped by a site identifier.
/// </summary>
/// <typeparam name="TEntity">The type of the site associated entity.</typeparam>
public abstract class SiteAssociatedRepository<TEntity> : AuditEntityRepository<TEntity>, ISiteAssociatedRepository<TEntity>
    where TEntity : ISiteAssociatedEntity
{
    /// <summary>
    /// Initializes a new instance of the SiteAssociatedRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context for the repository.</param>
    public SiteAssociatedRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

    /// <summary>
    /// Retrieves all entities associated with a specific site.
    /// </summary>
    /// <param name="siteId">The identifier of the site.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns a collection of site-associated entities.</returns>
    public async Task<IEnumerable<TEntity>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<TEntity>.Filter.Eq(x => x.SiteId, siteId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves an entity by its identifier, scoped to a specific site.
    /// </summary>
    /// <param name="id">The identifier of the entity.</param>
    /// <param name="siteId">The identifier of the site to which the entity is associated.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the site-associated entity if found.</returns>
    public async Task<TEntity?> GetByIdForSite(Guid id, Guid siteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idFilter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
        var siteIdFilter = Builders<TEntity>.Filter.Eq(x => x.SiteId, siteId);
        var combinedFilter = Builders<TEntity>.Filter.And(idFilter, siteIdFilter);
        var findResult = await Collection.FindAsync(combinedFilter, null, cancellationToken);
        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
