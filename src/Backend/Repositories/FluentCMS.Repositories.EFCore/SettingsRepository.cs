namespace FluentCMS.Repositories.EFCore;

public class SettingsRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : ISettingsRepository
{
    protected readonly FluentCmsDbContext DbContext = dbContext;
    protected readonly DbSet<Settings> DbSet = dbContext.Set<Settings>();

    public async Task<IEnumerable<Settings>> GetAll(CancellationToken cancellationToken = default)
    {
        var settingsList = await DbSet.Include(s => s.Values).ToListAsync(cancellationToken);

        foreach (var settings in settingsList)
            settings.Values = settings.Values.ToDictionary(sv => sv.Key, sv => sv.Value);

        return settingsList;
    }

    public async Task<Settings?> GetById(Guid entityId, CancellationToken cancellationToken = default)
    {
        var settings = await DbSet
             .Include(s => s.Values)
             .FirstOrDefaultAsync(s => s.Id == entityId, cancellationToken);

        if (settings != null)
            settings.Values = settings.Values.ToDictionary(sv => sv.Key, sv => sv.Value);

        return settings;
    }

    public async Task<IEnumerable<Settings>> GetByIds(IEnumerable<Guid> entityIds, CancellationToken cancellationToken = default)
    {
        var settingsList = await DbSet.Where(s => entityIds.Contains(s.Id)).Include(s => s.Values).ToListAsync(cancellationToken);

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
            existingSettings = new Settings
            {
                Id = entityId,
                Values = settings
            };

            SetAuditableFieldsForCreate(existingSettings);

            DbSet.Add(existingSettings);

            // Convert the dictionary to a list of SettingsValue entities
            foreach (var kvp in settings)
            {
                DbContext.Set<SettingsValue>().Add(new SettingsValue
                {
                    Id = Guid.NewGuid(),
                    SettingsId = entityId,
                    Key = kvp.Key,
                    Value = kvp.Value
                });
            }
        }
        else
        {
            SetAuditableFieldsForUpdate(existingSettings);
            var existingValues = existingSettings.Values;

            foreach (var kvp in settings)
                existingValues[kvp.Key] = kvp.Value;
        }

        // Save changes to the database
        await DbContext.SaveChangesAsync(cancellationToken);

        // Reload the entity with updated values
        existingSettings.Values = settings;

        return existingSettings;
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
