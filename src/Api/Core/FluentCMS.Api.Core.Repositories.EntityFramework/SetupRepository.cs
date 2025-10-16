namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class SetupRepository(CmsCoreDbContext dbContext) : ISetupRepository
{
    public async Task<bool> IsInitialized(CancellationToken cancellationToken = default)
    {
        // Check if the database exists by querying any required table or checking metadata
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            return false;

        if (!await dbContext.GlobalSettings.AnyAsync(cancellationToken))
            return false;

        return true;
    }
}
