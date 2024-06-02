namespace FluentCMS.Repositories.MongoDB;

public class GlobalSettingsRepository : IGlobalSettingsRepository
{
    private readonly IMongoCollection<GlobalSettings> _collection;
    private readonly IAuthContext _authContext;
    private readonly IMongoDBContext _mongoDbContext;

    public GlobalSettingsRepository(IMongoDBContext mongoDbContext, IAuthContext authContext)
    {
        _collection = mongoDbContext.Database.GetCollection<GlobalSettings>(nameof(GlobalSettings).ToLowerInvariant());
        _authContext = authContext;
        _mongoDbContext = mongoDbContext;
    }

    public async Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _collection.Find(Builders<GlobalSettings>.Filter.Empty).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<GlobalSettings?> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await Get(cancellationToken);

        if (existing == null)
            return await Create(settings, cancellationToken);

        SetAuditableFieldsForUpdate(settings);

        var idFilter = Builders<GlobalSettings>.Filter.Eq(x => x.Id, settings.Id);

        return await _collection.FindOneAndReplaceAsync(idFilter, settings, null, cancellationToken);
    }

    public async Task<bool> Reset(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var collections = await _mongoDbContext.Database.ListCollectionNamesAsync(cancellationToken: cancellationToken);

        foreach (var collectionName in collections.ToEnumerable(cancellationToken))
            await _mongoDbContext.Database.DropCollectionAsync(collectionName, cancellationToken);

        return true;
    }

    private async Task<GlobalSettings?> Create(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditableFieldsForCreate(settings);
        await _collection.InsertOneAsync(settings, null, cancellationToken);
        return settings;
    }

    private void SetAuditableFieldsForCreate(GlobalSettings settings)
    {
        settings.Id = Guid.NewGuid();
        settings.CreatedAt = DateTime.UtcNow;
        settings.CreatedBy = _authContext.Username;
    }

    private void SetAuditableFieldsForUpdate(GlobalSettings settings)
    {
        settings.ModifiedAt = DateTime.UtcNow;
        settings.ModifiedBy = _authContext.Username;
    }
}
