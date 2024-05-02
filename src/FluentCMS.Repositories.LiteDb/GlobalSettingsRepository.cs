using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using LiteDB;
using LiteDB.Async;

namespace FluentCMS.Repositories.LiteDb;

public class GlobalSettingsRepository(ILiteDBContext liteDbContext, IAuthContext authContext)
    : IGlobalSettingsRepository
{
    private readonly ILiteCollectionAsync<GlobalSettings> _collection = liteDbContext.Database.GetCollection<GlobalSettings>(nameof(GlobalSettings).ToLowerInvariant());

    public async Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await _collection.FindAsync(Query.All())).SingleOrDefault();
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

    public async Task<bool> Reset(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var collections = await liteDbContext.Database.GetCollectionNamesAsync();

        foreach (var collectionName in collections)
            await liteDbContext.Database.DropCollectionAsync(collectionName);

        return true;
    }

    private async Task<GlobalSettings?> Create(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetAuditableFieldsForCreate(settings);
        await _collection.InsertAsync(settings);
        return settings;
    }

    private void SetAuditableFieldsForCreate(GlobalSettings settings)
    {
        settings.Id = Guid.NewGuid();
        settings.CreatedAt = DateTime.UtcNow;
        settings.CreatedBy = authContext.Username;
    }

    private void SetAuditableFieldsForUpdate(GlobalSettings settings)
    {
        settings.ModifiedAt = DateTime.UtcNow;
        settings.ModifiedBy = authContext.Username;
    }
}
