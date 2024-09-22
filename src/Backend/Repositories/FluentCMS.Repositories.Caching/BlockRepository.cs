namespace FluentCMS.Repositories.Caching;

public class BlockRepository(IBlockRepository repository, ICacheProvider cacheProvider) : SiteAssociatedRepository<Block>(repository, cacheProvider), IBlockRepository
{
}
