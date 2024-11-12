namespace FluentCMS.Repositories.EFCore;

public class SettingsRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : ISettingsRepository
{
    public async Task<IEnumerable<Settings>> GetAll(CancellationToken cancellationToken = default)
    {
        var settingsList = await dbContext.Settings.ToListAsync(cancellationToken);
        var settingValues = await dbContext.SettingValues.ToListAsync(cancellationToken);

        // set the Values property of each Settings entity to a dictionary of its SettingValues
        foreach (var settings in settingsList)
            settings.Values = settingValues.Where(sv => sv.SettingsId == settings.Id).ToDictionary(sv => sv.Key, sv => sv.Value);

        return settingsList;
    }

    public async Task<Settings?> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        var settings = await dbContext.Settings.FirstOrDefaultAsync(s => s.Id == entityId, cancellationToken);

        if (settings != null)
            settings.Values = dbContext.SettingValues.Where(x => x.SettingsId == settings.Id).ToDictionary(sv => sv.Key, sv => sv.Value);

        return settings;
    }

    public async Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        var settingsList = await dbContext.Settings.Where(s => entityIds.Contains(s.Id)).ToListAsync(cancellationToken);
        var settingValues = await dbContext.SettingValues.Where(sv => entityIds.Contains(sv.SettingsId)).ToListAsync(cancellationToken);

        // set the Values property of each Settings entity to a dictionary of its SettingValues
        foreach (var settings in settingsList)
            settings.Values = settingValues.Where(sv => sv.SettingsId == settings.Id).ToDictionary(sv => sv.Key, sv => sv.Value);

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

            // Add each dictionary entry to the SettingValues table
            foreach (var kvp in settings)
            {
                var settingValue = new SettingValue
                {
                    Id = Guid.NewGuid(),
                    SettingsId = newSettings.Id,
                    Key = kvp.Key,
                    Value = kvp.Value
                };
                dbContext.SettingValues.Add(settingValue);
            }
            // Save changes to the database
            await dbContext.SaveChangesAsync(cancellationToken);

            return newSettings;
        }
        else
        {
            SetAuditableFieldsForUpdate(existingSettings);

            // Update any properties in the Settings entity if needed
            dbContext.Entry(existingSettings).CurrentValues.SetValues(settings);

            // Remove existing values from SettingValues table
            var existingValues = dbContext.SettingValues.Where(sv => sv.SettingsId == existingSettings.Id);
            dbContext.SettingValues.RemoveRange(existingValues);

            // Add updated values to SettingValues table
            foreach (var kvp in settings)
            {
                var settingValue = new SettingValue
                {
                    Id = Guid.NewGuid(),
                    SettingsId = existingSettings.Id,
                    Key = kvp.Key,
                    Value = kvp.Value
                };
                dbContext.SettingValues.Add(settingValue);
            }

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
