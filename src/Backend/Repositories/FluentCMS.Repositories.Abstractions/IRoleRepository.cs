namespace FluentCMS.Repositories.Abstractions;

public interface IRoleRepository : ISiteAssociatedRepository<Role>
{
    Task<Role> GetByNameAndSiteId(Guid siteId, string name, CancellationToken cancellationToken = default);
}
