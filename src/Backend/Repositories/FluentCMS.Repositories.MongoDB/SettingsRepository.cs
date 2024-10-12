namespace FluentCMS.Repositories.MongoDB;

public class SettingsRepository : ISettingsRepository
{
    private readonly IMongoCollection<Settings> _collection;
    private readonly IApiExecutionContext _apiExecutionContext;
    private readonly IMongoDBContext _mongoDbContext;

    public SettingsRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext)
    {
        _collection = mongoDbContext.Database.GetCollection<Settings>(nameof(Settings).ToLowerInvariant());
        _apiExecutionContext = apiExecutionContext;
        _mongoDbContext = mongoDbContext;

        // Ensure index on Id field
        var idIndex = Builders<Settings>.IndexKeys.Ascending(x => x.Id);
        _collection.Indexes.CreateOne(new CreateIndexModel<Settings>(idIndex));
    }

    public virtual async Task<IEnumerable<Settings>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Builders<Settings>.Filter.Empty;
        var findResult = await _collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }

    public async Task<Settings?> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var idFilter = Builders<Settings>.Filter.Eq(x => x.Id, entityId);
        var findResult = await _collection.FindAsync(idFilter, null, cancellationToken);
        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var idFilter = Builders<Settings>.Filter.In(x => x.Id, entityIds);
        var findResult = await _collection.FindAsync(idFilter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }

    public async Task<Settings?> Update(Guid entityId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await GetById(entityId, cancellationToken);

        if (existing != null)
        {
            SetAuditableFieldsForUpdate(existing);

            // add new settings to existing settings or update existing settings
            foreach (var setting in settings)
                existing.Values[setting.Key] = setting.Value;

            var idFilter = Builders<Settings>.Filter.Eq(x => x.Id, entityId);
            await _collection.ReplaceOneAsync(idFilter, existing, cancellationToken: cancellationToken);
            return existing;
        }
        else
        {
            var newSettings = new Settings
            {
                Id = entityId,
                Values = settings
            };
            SetAuditableFieldsForCreate(newSettings);
            var options = new InsertOneOptions { BypassDocumentValidation = false };
            await _collection.InsertOneAsync(newSettings, options, cancellationToken);
            return newSettings;
        }
    }

    private void SetAuditableFieldsForCreate(Settings settings)
    {
        settings.CreatedAt = DateTime.UtcNow;
        settings.CreatedBy = _apiExecutionContext.Username;
    }

    private void SetAuditableFieldsForUpdate(Settings settings)
    {
        settings.ModifiedAt = DateTime.UtcNow;
        settings.ModifiedBy = _apiExecutionContext.Username;
    }

}
