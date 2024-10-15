using System;

namespace FluentCMS.Repositories.RavenDB;

public class SettingsRepository : AuditableEntityRepository<Settings>, ISettingsRepository
{
    public SettingsRepository(IRavenDBContext dbContext, IApiExecutionContext apiExecutionContext) : base(dbContext, apiExecutionContext)
    {
    }

    public async Task<Settings?> Update(Guid entityId, Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        using (var session = Store.OpenAsyncSession())
        {
            var existing = await session.Query<RavenEntity<Settings>>().SingleOrDefaultAsync(x => x.Data.Id == entityId, cancellationToken);

            if (existing == null)
            {
                if (entityId == Guid.Empty)
                {
                    entityId = Guid.NewGuid();
                }

                var setting = new Settings()
                {
                    Id = entityId,
                    Values = settings
                };

                SetAuditableFieldsForCreate(setting);

                existing = new RavenEntity<Settings>(setting);

                await session.StoreAsync(existing, cancellationToken);
            }
            else
            {
                // add new settings to existing settings or update existing settings
                foreach (var setting in settings)
                    existing.Data.Values[setting.Key] = setting.Value;

                SetAuditableFieldsForUpdate(existing.Data, existing.Data);
            }

            await session.SaveChangesAsync();

            return existing.Data;
        }
    }
}
