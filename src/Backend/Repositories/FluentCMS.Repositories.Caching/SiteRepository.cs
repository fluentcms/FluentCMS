namespace FluentCMS.Repositories.Caching;

public class SiteRepository(ISiteRepository repository, ICacheProvider cacheProvider) : AuditableEntityRepository<Site>(repository, cacheProvider), ISiteRepository
{
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        var sites = await GetAll(cancellationToken);
        return sites.FirstOrDefault(s => s.Urls.Contains(url));
    }
}
