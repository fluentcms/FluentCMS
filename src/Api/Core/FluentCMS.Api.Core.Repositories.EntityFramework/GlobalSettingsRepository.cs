namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class GlobalSettingsRepository(CmsCoreDbContext dbContext) : Repository<GlobalSettings, CmsCoreDbContext>(dbContext), IGlobalSettingsRepository
{
    public async Task<GlobalSettings> Get(CancellationToken cancellationToken = default)
    {
        return await Query().First(cancellationToken);
    }

    public override async Task<GlobalSettings> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        var existingSettings = await Get(cancellationToken);

        if (existingSettings == null)
        {
            return await Add(settings, cancellationToken);
        }
        else
        {
            settings.Id = existingSettings.Id; // Ensure the ID remains the same for update
            return await Update(settings, cancellationToken);
        }
    }
}
