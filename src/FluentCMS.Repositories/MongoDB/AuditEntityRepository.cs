using FluentCMS.Entities;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Extends the base EntityRepository to include audit functionality for entities.
/// This repository automatically sets audit fields such as creation and update timestamps.
/// </summary>
/// <typeparam name="TEntity">The type of the audit entity.</typeparam>
public abstract class AuditEntityRepository<TEntity> : EntityRepository<TEntity>, IAuditEntityRepository<TEntity>
    where TEntity : IAuditEntity
{

    /// <summary>
    /// Initializes a new instance of the AuditEntityRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context to access user information.</param>
    public AuditEntityRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

    /// <summary>
    /// Creates a new audit entity in the collection with audit fields set.
    /// </summary>
    /// <param name="entity">The entity to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the created entity.</returns>
    public override async Task<TEntity?> Create(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditFieldsForCreate(entity);
        return await base.Create(entity, cancellationToken);
    }

    /// <summary>
    /// Creates multiple new audit entities in the collection with audit fields set.
    /// </summary>
    /// <param name="entities">The collection of entities to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the collection of created entities.</returns>
    public override async Task<IEnumerable<TEntity>> CreateMany(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var entity in entities)
        {
            SetAuditFieldsForCreate(entity);
        }
        return await base.CreateMany(entities, cancellationToken);
    }

    /// <summary>
    /// Updates an existing audit entity in the collection with updated audit fields.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the updated entity.</returns>
    public override async Task<TEntity?> Update(TEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditFieldsForUpdate(entity);
        return await base.Update(entity, cancellationToken);
    }

    /// <summary>
    /// Sets the audit fields when creating a new entity.
    /// </summary>
    /// <param name="entity">The entity to set audit fields for.</param>
    private void SetAuditFieldsForCreate(TEntity entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.CreatedBy = AppContext.Username;
        entity.LastUpdatedAt = entity.CreatedAt;
        entity.LastUpdatedBy = entity.CreatedBy;
    }

    /// <summary>
    /// Sets the audit fields when updating an existing entity.
    /// </summary>
    /// <param name="entity">The entity to set audit fields for.</param>
    private void SetAuditFieldsForUpdate(TEntity entity)
    {
        entity.LastUpdatedAt = DateTime.UtcNow;
        entity.LastUpdatedBy = AppContext.Username;
    }
}
