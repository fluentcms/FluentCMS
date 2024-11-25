namespace FluentCMS.Repositories.EFCore;

public class SettingsRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext, IMapper mapper) : ISettingsRepository
{
    public async Task<IEnumerable<Settings>> GetAll(CancellationToken cancellationToken = default)
    {
        var dbSettings = await dbContext.Settings.ToListAsync(cancellationToken);
        return mapper.Map<IEnumerable<Settings>>(dbSettings);
    }

    public async Task<Settings?> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        var dbSettings = await dbContext.Settings.FirstOrDefaultAsync(ct => ct.Id == entityId, cancellationToken);
        return dbSettings != null ? mapper.Map<Settings>(dbSettings) : null;
    }

    public async Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        var dbSettings = await dbContext.Settings.Where(ct => entityIds.Contains(ct.Id)).ToListAsync(cancellationToken);
        return mapper.Map<IEnumerable<Settings>>(dbSettings);
    }

    public async Task<Settings?> Update(Guid entityId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        var existingDbSettings = await dbContext.Settings.FirstOrDefaultAsync(ct => ct.Id == entityId, cancellationToken);

        if (existingDbSettings == null)
        {
            // Create a new Settings entity if it doesn't exist
            var newDbSettings = new SettingsModel
            {
                Id = entityId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = apiExecutionContext.Username,
                Values = settings.Where(x => !string.IsNullOrEmpty(x.Value)).Select(x=> new SettingValuesModel
                {
                    Id = Guid.NewGuid(),
                    Key = x.Key,
                    Value = x.Value,
                    SettingId = entityId
                } ).ToList()
            };

            dbContext.Settings.Add(newDbSettings);
            // Save changes to the database
            await dbContext.SaveChangesAsync(cancellationToken);

            return mapper.Map<Settings>(newDbSettings);
        }
        else
        {
            existingDbSettings.ModifiedAt = DateTime.UtcNow;
            existingDbSettings.ModifiedBy = apiExecutionContext.Username;

            // Add updated values to SettingValues table
            foreach (var kvp in settings.Where(x => !string.IsNullOrEmpty(x.Value)))
            {
                var settingValue = existingDbSettings.Values.FirstOrDefault(x => x.Key == kvp.Key);
                if (settingValue == null)
                {
                    // Create a new SettingValues entity if it doesn't exist
                    settingValue = new SettingValuesModel
                    {
                        Id = Guid.NewGuid(),
                        SettingId = existingDbSettings.Id,
                        Key = kvp.Key,
                        Value = kvp.Value
                    };
                    existingDbSettings.Values.Add(settingValue);
                }
                else
                {
                    // Update the existing SettingValues entity
                    settingValue.Value = kvp.Value;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return mapper.Map<Settings>(existingDbSettings);
        }
    }
}
