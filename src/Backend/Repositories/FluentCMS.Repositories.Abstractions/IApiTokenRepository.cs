
namespace FluentCMS.Repositories.Abstractions;

public interface IApiTokenRepository : IAuditableEntityRepository<ApiToken>
{
    Task<ApiToken> GetByApiKeyAsync(string apiKey, CancellationToken cancellationToken);

}
