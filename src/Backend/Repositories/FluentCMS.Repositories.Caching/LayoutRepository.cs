namespace FluentCMS.Repositories.Caching;

public class LayoutRepository(ILayoutRepository repository, ICacheProvider cacheProvider) : SiteAssociatedRepository<Layout>(repository, cacheProvider), ILayoutRepository
{
}
