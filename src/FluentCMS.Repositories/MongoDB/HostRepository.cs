using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Repository for managing Host entity in MongoDB.
/// </summary>
public class HostRepository : IHostRepository
{
    private readonly IMongoCollection<Host> _collection;
    private readonly IApplicationContext _applicationContext;

    /// <summary>
    /// Initializes a new instance of the HostRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context to access user information.</param>
    public HostRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
    {
        _collection = mongoDbContext.Database.GetCollection<Host>("host");
        _applicationContext = applicationContext;
    }

    /// <summary>
    /// Creates a new Host entity in the database.
    /// </summary>
    /// <param name="host">The Host entity to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the created Host entity.</returns>
    public async Task<Host?> Create(Host host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditFieldsForCreate(host);
        await _collection.InsertOneAsync(host, null, cancellationToken);
        return host;
    }

    /// <summary>
    /// Retrieves a single Host entity from the database.
    /// Assumes only one Host entity is present.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the Host entity, if found.</returns>
    public async Task<Host?> Get(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _collection.Find(Builders<Host>.Filter.Empty).SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Updates an existing Host entity in the database.
    /// </summary>
    /// <param name="host">The Host entity to update.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the updated Host entity.</returns>
    public async Task<Host?> Update(Host host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditFieldsForUpdate(host);
        var idFilter = Builders<Host>.Filter.Eq(x => x.Id, host.Id);
        return await _collection.FindOneAndReplaceAsync(idFilter, host, null, cancellationToken);
    }

    /// <summary>
    /// Sets the audit fields when creating a new Host entity.
    /// </summary>
    /// <param name="host">The Host entity to set audit fields for.</param>
    private void SetAuditFieldsForCreate(Host host)
    {
        host.Id = Guid.NewGuid();
        host.CreatedAt = DateTime.UtcNow;
        host.CreatedBy = _applicationContext.Username;
        host.LastUpdatedAt = host.CreatedAt;
        host.LastUpdatedBy = host.CreatedBy;
    }

    /// <summary>
    /// Sets the audit fields when updating an existing Host entity.
    /// </summary>
    /// <param name="host">The Host entity to set audit fields for.</param>
    private void SetAuditFieldsForUpdate(Host host)
    {
        host.LastUpdatedAt = DateTime.UtcNow;
        host.LastUpdatedBy = _applicationContext.Username;
    }
}
