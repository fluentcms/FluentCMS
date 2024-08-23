namespace FluentCMS.Repositories.Abstractions;

public interface IRoleRepository : ISiteAssociatedRepository<Role>
{
    Task<bool> RoleBySameNameIsExistInSite(Guid siteId, string name, CancellationToken cancellationToken = default);
}
