namespace FluentCMS.Repositories.Caching;

public class PageRepository(IPageRepository repository, ICacheProvider cacheProvider) : SiteAssociatedRepository<Page>(repository, cacheProvider), IPageRepository
{
}
