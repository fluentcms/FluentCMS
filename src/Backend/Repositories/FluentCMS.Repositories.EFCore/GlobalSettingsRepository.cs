namespace FluentCMS.Repositories.EFCore;

public class GlobalSettingsRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : IGlobalSettingsRepository
{
    public async Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {

        return await _collection.Query().SingleOrDefaultAsync();
    }

    public async Task<GlobalSettings?> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        var existing = await Get(cancellationToken);

        if (existing == null)
            return await Create(settings, cancellationToken);

        SetAuditableFieldsForUpdate(settings);

        await _collection.UpdateAsync(settings.Id, settings);

        return settings;
    }

    private async Task<GlobalSettings?> Create(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        SetAuditableFieldsForCreate(settings);
        await _collection.InsertAsync(settings);
        return settings;
    }

    public async Task<bool> Initialized(CancellationToken cancellationToken = default)
    {
        var existing = await Get(cancellationToken);

        return existing?.Initialized ?? false;
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
