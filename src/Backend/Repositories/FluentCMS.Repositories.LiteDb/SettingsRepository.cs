
namespace FluentCMS.Repositories.LiteDb;

public class SettingsRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : ISettingsRepository
{
    private readonly ILiteCollectionAsync<Settings> _collection = liteDbContext.Database.GetCollection<Settings>(nameof(Settings).ToLowerInvariant());

    public virtual async Task<IEnumerable<Settings>> GetAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _collection.Query().ToListAsync() ?? [];
    }

    public async Task<Settings?> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _collection.Query().Where(x => x.Id == entityId).SingleOrDefaultAsync();
    }

    public async Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var findResult = await _collection.Query().Where(x => entityIds.Contains(x.Id)).ToListAsync() ?? [];
        return findResult;
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

            await _collection.UpdateAsync(existing.Id, existing);
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
            await _collection.InsertAsync(newSettings);
            return newSettings;
        }
    }

    private void SetAuditableFieldsForCreate(Settings settings)
    {
        settings.CreatedAt = DateTime.UtcNow;
        settings.CreatedBy = apiExecutionContext.Username;
    }

    private void SetAuditableFieldsForUpdate(Settings settings)
    {
        settings.ModifiedAt = DateTime.UtcNow;
        settings.ModifiedBy = apiExecutionContext.Username;
    }
}
