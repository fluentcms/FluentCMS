namespace FluentCMS.Repositories.LiteDb;

public class ApiTokenRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ApiToken>(liteDbContext, apiExecutionContext),
    IApiTokenRepository
{
    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.Query().Where(x => x.Key == apiKey).SingleOrDefaultAsync();
    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.Name == name).FirstOrDefaultAsync();
    }
}
