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
            var existing = await session.Query<Settings>().SingleOrDefaultAsync(x => x.Id == entityId, cancellationToken);

            if (existing == null)
            {
                if (entityId == Guid.Empty)
                {
                    entityId = Guid.NewGuid();
                }

                existing = new Settings()
                {
                    Id = entityId,
                    Values = settings
                };

                SetAuditableFieldsForCreate(existing);

                await session.StoreAsync(existing);
            }
            else
            {
                // add new settings to existing settings or update existing settings
                foreach (var setting in settings)
                    existing.Values[setting.Key] = setting.Value;

                SetAuditableFieldsForUpdate(existing, existing);
            }

            await session.SaveChangesAsync();

            return existing;
        }
    }
}
