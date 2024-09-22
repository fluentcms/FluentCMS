namespace FluentCMS.Repositories.Caching;

public class GlobalSettingsRepository(IGlobalSettingsRepository globalSettingsRepository, ICacheProvider cacheProvider) : IGlobalSettingsRepository
{
    private static string CacheKey => $"{nameof(GlobalSettings)}";

    public async Task<GlobalSettings?> Get(CancellationToken cancellationToken = default)
    {
        return await GetCached(cancellationToken);
    }

    public async Task<bool> Initialized(CancellationToken cancellationToken = default)
    {
        return await GetCached(cancellationToken) != null;
    }

    public async Task<GlobalSettings?> Update(GlobalSettings settings, CancellationToken cancellationToken = default)
    {
        var updated = await globalSettingsRepository.Update(settings, cancellationToken);
        InvalidateCache();
        return updated;
    }

    private async Task<GlobalSettings?> GetCached(CancellationToken cancellationToken = default)
    {
        if (!cacheProvider.TryGetValue(CacheKey, out GlobalSettings? globalSettings))
        {
            // if not, get the entities from the database
            globalSettings = await globalSettingsRepository.Get(cancellationToken);

            // cache the entities
            cacheProvider.Set(CacheKey, globalSettings);
        }
        return globalSettings;
    }

    private void InvalidateCache()
    {
        cacheProvider.Remove(CacheKey);
    }

}
