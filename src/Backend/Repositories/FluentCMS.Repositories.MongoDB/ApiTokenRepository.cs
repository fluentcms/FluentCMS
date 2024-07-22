

using DnsClient.Protocol;

namespace FluentCMS.Repositories.MongoDB;

public class ApiTokenRepository(
    IMongoDBContext mongoDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<ApiToken>(mongoDbContext, authContext),
    IApiTokenRepository
{
    public override Task<ApiToken?> Create(ApiToken entity, CancellationToken cancellationToken = default)
    {
        entity.ApiKey = GenerateApiKey();
        return base.Create(entity, cancellationToken);
    }

    private string? GenerateApiKey()
    {
        return Guid.NewGuid().ToString();
    }

}
