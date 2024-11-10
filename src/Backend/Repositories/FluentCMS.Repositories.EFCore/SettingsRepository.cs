namespace FluentCMS.Repositories.EFCore;

public class SettingsRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : ISettingsRepository
{
    public async Task<IEnumerable<Settings>> GetAll(CancellationToken cancellationToken = default)
    {
        var settingsList = await dbContext.Settings.Include(s => s.Values).ToListAsync(cancellationToken);

        foreach (var settings in settingsList)
            settings.Values = settings.Values.ToDictionary(sv => sv.Key, sv => sv.Value);

        return settingsList;
    }

    public async Task<Settings?> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        var settings = await dbContext.Settings.Include(s => s.Values).FirstOrDefaultAsync(s => s.Id == entityId, cancellationToken);

        if (settings != null)
            settings.Values = settings.Values.ToDictionary(sv => sv.Key, sv => sv.Value);

        return settings;
    }

    public async Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        var settingsList = await dbContext.Settings.Where(s => entityIds.Contains(s.Id)).Include(s => s.Values).ToListAsync(cancellationToken);

        foreach (var settings in settingsList)
            settings.Values = settings.Values.ToDictionary(sv => sv.Key, sv => sv.Value);

        return settingsList;
    }

    public async Task<Settings?> Update(Guid entityId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        // Retrieve the Settings entity, including its related SettingsValues
        var existingSettings = await GetById(entityId, cancellationToken);

        if (existingSettings == null)
        {
            // Create a new Settings entity if it doesn't exist
            var newSettings = new Settings
            {
                Id = entityId,
                Values = settings
            };

            SetAuditableFieldsForCreate(newSettings);
            dbContext.Settings.Add(newSettings);

            // Save changes to the database
            await dbContext.SaveChangesAsync(cancellationToken);

            return newSettings;
        }
        else
        {
            SetAuditableFieldsForUpdate(existingSettings);
            var existingValues = existingSettings.Values;

            foreach (var kvp in settings)
                existingValues[kvp.Key] = kvp.Value;

            // Save changes to the database
            await dbContext.SaveChangesAsync(cancellationToken);

            return existingSettings;
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
