namespace FluentCMS.Repositories.Caching;

public class SetupRepository(ISetupRepository setupRepository, ICacheProvider cacheProvider) : ISetupRepository
{
    private static string CacheKey => $"{nameof(SetupRepository)}";

    public async Task<bool> Initialized(CancellationToken cancellationToken = default)
    {
        return await GetCached(cancellationToken);
    }

    public async Task<bool> InitializeDb(CancellationToken cancellationToken = default)
    {
        var result = await setupRepository.InitializeDb(cancellationToken);
        cacheProvider.Set(CacheKey, result);
        return result;
    }

    private async Task<bool> GetCached(CancellationToken cancellationToken = default)
    {
        if (!cacheProvider.TryGetValue(CacheKey, out bool isInitiliazed))
        {
            isInitiliazed = await setupRepository.Initialized(cancellationToken);
            // cache the entities
            cacheProvider.Set(CacheKey, isInitiliazed);
        }
        return isInitiliazed;
    }

    private void InvalidateCache()
    {
        cacheProvider.Remove(CacheKey);
    }
}
