namespace FluentCMS.Repositories.Postgres.Repositories;

public class SettingsRepository(PostgresDbContext postgresDbContext) : AuditableEntityRepository<Settings>(postgresDbContext), ISettingsRepository, IService
{

    public async Task<Settings?> Update(Guid entityId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await GetById(entityId, cancellationToken);

        if (existing != null)
        {

            // add new settings to existing settings or update existing settings
            foreach (var setting in settings)
                existing.Values[setting.Key] = setting.Value;

            await Update(existing, cancellationToken);
            return existing;
        }

        var newSettings = new Settings
        {
            Id = entityId,
            Values = settings
        };
        await Create(newSettings, cancellationToken);
        return newSettings;
    }


}
