namespace FluentCMS.Repositories.EFCore;

public class SettingsRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : ISettingsRepository
{
    protected readonly FluentCmsDbContext DbContext = dbContext;
    protected readonly DbSet<Settings> DbSet = dbContext.Set<Settings>();

    public async Task<IEnumerable<Settings>> GetAll(CancellationToken cancellationToken = default)
    {
        // Fetch all settings
        var settingsList = await DbSet.ToListAsync(cancellationToken);

        // Fetch all SettingValues and group them by SettingsId
        var values = await DbContext.SettingValues.ToListAsync(cancellationToken);

        var valuesGroupedBySettingsId = values.GroupBy(sv => sv.SettingsId).ToDictionary(g => g.Key, g => g.ToDictionary(sv => sv.Key, sv => sv.Value));

        // Map grouped SettingValues to their respective Settings entities
        foreach (var settings in settingsList)
        {
            if (valuesGroupedBySettingsId.TryGetValue(settings.Id, out var valueDictionary))
            {
                settings.Values = valueDictionary;
            }
            else
            {
                settings.Values = [];
            }
        }

        return settingsList;
    }

    public async Task<Settings?> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        var settings = await DbSet.FindAsync([entityId], cancellationToken);
        if (settings != null)
        {
            var values = await DbContext.SettingValues.Where(sv => sv.SettingsId == entityId).ToListAsync(cancellationToken);
            settings.Values = values.ToDictionary(sv => sv.Key, sv => sv.Value);
        }
        return settings;
    }

    public async Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        var settingsList = await DbSet.Where(settings => entityIds.Contains(settings.Id)).ToListAsync(cancellationToken);

        // Fetch associated SettingValues for each setting
        var values = await DbContext.SettingValues.Where(sv => entityIds.Contains(sv.SettingsId)).ToListAsync(cancellationToken);

        // Group the SettingValues by SettingsId and map them back to their respective Settings entities
        var valuesGroupedBySettingsId = values.GroupBy(sv => sv.SettingsId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(sv => sv.Key, sv => sv.Value));

        foreach (var settings in settingsList)
        {
            if (valuesGroupedBySettingsId.TryGetValue(settings.Id, out var valueDictionary))
            {
                settings.Values = valueDictionary;
            }
            else
            {
                settings.Values = [];
            }
        }

        return settingsList;
    }

    public async Task<Settings?> Update(Guid entityId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        // Attempt to retrieve the existing Settings entity
        // Ensure we have the current Values if using EF core shadow properties or related tables
        var existingSettings = await DbSet.Include(s => s.Values).FirstOrDefaultAsync(s => s.Id == entityId, cancellationToken);

        if (existingSettings == null)
        {
            // If no existing entity, create a new one
            var newSettings = new Settings
            {
                Id = entityId,
                Values = new Dictionary<string, string>(settings) // Initialize with provided settings
            };

            SetAuditableFieldsForCreate(newSettings);

            DbSet.Add(newSettings);

            var settingValues = settings
                .Select(kv => new SettingValue
                {
                    SettingsId = entityId,
                    Key = kv.Key,
                    Value = kv.Value
                });

            DbContext.SettingValues.AddRange(settingValues);
            await DbContext.SaveChangesAsync(cancellationToken);

            return newSettings;
        }
        else
        {
            SetAuditableFieldsForUpdate(existingSettings);

            // If entity exists, update the Values dictionary
            var existingValues = await DbContext.SettingValues.Where(sv => sv.SettingsId == entityId).ToListAsync(cancellationToken);

            // Remove existing SettingValues not present in the new dictionary
            var valuesToRemove = existingValues.Where(ev => !settings.ContainsKey(ev.Key)).ToList();

            DbContext.SettingValues.RemoveRange(valuesToRemove);

            // Update existing SettingValues and add new ones
            foreach (var kv in settings)
            {
                var existingValue = existingValues.FirstOrDefault(ev => ev.Key == kv.Key);
                if (existingValue != null)
                {
                    existingValue.Value = kv.Value;
                }
                else
                {
                    DbContext.SettingValues.Add(new SettingValue
                    {
                        SettingsId = entityId,
                        Key = kv.Key,
                        Value = kv.Value
                    });
                }
            }

            await DbContext.SaveChangesAsync(cancellationToken);

            // Update the Values dictionary in memory as well
            existingSettings.Values = new Dictionary<string, string>(settings);
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
