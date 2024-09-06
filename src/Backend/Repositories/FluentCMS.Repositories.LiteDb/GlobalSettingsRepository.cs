namespace FluentCMS.Repositories.LiteDb;

public class GlobalSettingsRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : IGlobalSettingsRepository
{
    private readonly ILiteCollectionAsync<GlobalSettings> _collection = liteDbContext.Database.GetCollection<GlobalSettings>(nameof(GlobalSettings).ToLowerInvariant());

    public async Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _collection.Query().SingleOrDefaultAsync();
    }

    public async Task<GlobalSettings?> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await Get(cancellationToken);

        if (existing == null)
            return await Create(settings, cancellationToken);

        SetAuditableFieldsForUpdate(settings);

        await _collection.UpdateAsync(settings.Id, settings);

        return settings;
    }

    private async Task<GlobalSettings?> Create(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditableFieldsForCreate(settings);
        await _collection.InsertAsync(settings);
        return settings;
    }

    public async Task<bool> Any(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var collections = await liteDbContext.Database.GetCollectionNamesAsync();

        return collections.Any();
    }

    private void SetAuditableFieldsForCreate(GlobalSettings settings)
    {
        settings.Id = Guid.NewGuid();
        settings.CreatedAt = DateTime.UtcNow;
        settings.CreatedBy = apiExecutionContext.Username;
    }

    private void SetAuditableFieldsForUpdate(GlobalSettings settings)
    {
        settings.ModifiedAt = DateTime.UtcNow;
        settings.ModifiedBy = apiExecutionContext.Username;
    }
}
