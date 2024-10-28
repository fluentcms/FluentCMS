namespace FluentCMS.Repositories.Caching;

public class FileRepository(IFileRepository repository, ICacheProvider cacheProvider) : SiteAssociatedRepository<Entities.File>(repository, cacheProvider), IFileRepository
{
}
