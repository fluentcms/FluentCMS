using Microsoft.Extensions.Caching.Memory;

namespace FluentCMS.Repositories.Caching;

public class ApiTokenRepository(IAuditableEntityRepository<ApiToken> repository, IMemoryCache memoryCache) : AuditableEntityRepository<ApiToken>(repository, memoryCache), IApiTokenRepository
{
    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken)
    {
        var apiKeys = await GetAll(cancellationToken);
        return apiKeys.FirstOrDefault(x => x.Key == apiKey);
    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken)
    {
        var apiKeys = await GetAll(cancellationToken);
        return apiKeys.FirstOrDefault(x => x.Name == name);
    }
}
