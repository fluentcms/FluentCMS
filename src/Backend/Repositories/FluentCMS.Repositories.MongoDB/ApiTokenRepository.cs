namespace FluentCMS.Repositories.MongoDB;

public class ApiTokenRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ApiToken>(mongoDbContext, apiExecutionContext), IApiTokenRepository
{
    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apiKeyFilter = Builders<ApiToken>.Filter.Eq(x => x.Key, apiKey);
        var findResult = await Collection.FindAsync(apiKeyFilter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.Name == name).FirstOrDefaultAsync(cancellationToken);
    }
}
