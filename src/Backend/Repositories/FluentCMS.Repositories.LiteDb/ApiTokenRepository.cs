

namespace FluentCMS.Repositories.LiteDb;

public class ApiTokenRepository(
    ILiteDBContext liteDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<ApiToken>(liteDbContext, authContext),
    IApiTokenRepository
{
    public Task<ApiToken> GetByApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

}
