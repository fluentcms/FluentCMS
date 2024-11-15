namespace FluentCMS.Repositories.EFCore;

public class GlobalSettingsRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : IGlobalSettingsRepository
{

    public async Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {
        return await dbContext.GlobalSettings.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<GlobalSettings?> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        var existingSettings = await Get(cancellationToken);

        if (existingSettings == null)
            return await Create(settings, cancellationToken);

        // since we have always one record on the table, the id is always the same
        settings.Id = existingSettings.Id;

        SetAuditableFieldsForUpdate(settings);

        dbContext.GlobalSettings.Update(settings);

        await dbContext.SaveChangesAsync(cancellationToken);

        return settings;
    }

    private async Task<GlobalSettings?> Create(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        SetAuditableFieldsForCreate(settings);
        dbContext.GlobalSettings.Add(settings);
        await dbContext.SaveChangesAsync(cancellationToken);

        return settings;
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
