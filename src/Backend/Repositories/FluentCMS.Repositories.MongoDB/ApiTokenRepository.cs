namespace FluentCMS.Repositories.MongoDB;

public class ApiTokenRepository(
    IMongoDBContext mongoDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<ApiToken>(mongoDbContext, authContext),
    IApiTokenRepository
{
    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apiKeyFilter = Builders<ApiToken>.Filter.Eq(x => x.Key, apiKey);
        var findResult = await Collection.FindAsync(apiKeyFilter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
