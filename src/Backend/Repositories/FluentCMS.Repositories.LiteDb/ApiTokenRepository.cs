
namespace FluentCMS.Repositories.LiteDb;

public class ApiTokenRepository(
    ILiteDBContext liteDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<ApiToken>(liteDbContext, authContext),
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
