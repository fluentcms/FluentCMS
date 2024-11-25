namespace FluentCMS.Repositories.EFCore;

public class GlobalSettingsRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext, IMapper mapper) : IGlobalSettingsRepository
{
    public async Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {
        var dbEntities = await dbContext.GlobalSettings.FirstOrDefaultAsync(cancellationToken);
        return mapper.Map<GlobalSettings>(dbEntities);
    }

    public async Task<GlobalSettings?> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        var existingSettings = await Get(cancellationToken);

        if (existingSettings == null)
        {
            SetAuditableFieldsForCreate(settings);

            var dbEntities = mapper.Map<GlobalSettingsModel>(settings);

            dbContext.GlobalSettings.Add(dbEntities);
            await dbContext.SaveChangesAsync(cancellationToken);

            return mapper.Map(dbEntities, settings);
        }
        else
        {
            // since we have always one record on the table, the id is always the same
            settings.Id = existingSettings.Id;
            SetAuditableFieldsForUpdate(settings);

            var dbEntities = mapper.Map(settings, existingSettings);
            await dbContext.SaveChangesAsync(cancellationToken);
            return mapper.Map(dbEntities, settings);
        }
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
