namespace FluentCMS.Repositories.Caching;

public class RoleRepository(IRoleRepository repository, ICacheProvider cacheProvider) : SiteAssociatedRepository<Role>(repository, cacheProvider), IRoleRepository
{
}
