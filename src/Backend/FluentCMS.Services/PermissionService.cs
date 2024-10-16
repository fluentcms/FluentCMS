namespace FluentCMS.Services;

public interface IPermissionService : IAutoRegisterService
{
    Task<IEnumerable<Permission>> Set(Guid siteId, SitePermissionAction action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<Permission>> Set(Guid siteId, Guid pageId, PagePermissionAction action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<Permission>> Get(Guid siteId, SitePermissionAction action, CancellationToken cancellationToken = default);
    Task<IEnumerable<Permission>> Get(Guid siteId, Guid pageId, PagePermissionAction action, CancellationToken cancellationToken = default);
}

public class PermissionService(IPermissionRepository permissionRepository) : IPermissionService
{
    public async Task<IEnumerable<Permission>> Set(Guid siteId, SitePermissionAction action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        return await permissionRepository.Set(siteId, siteId, nameof(Site), action.ToString(), roleIds, cancellationToken);
    }

    public async Task<IEnumerable<Permission>> Set(Guid siteId, Guid pageId, PagePermissionAction action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        return await permissionRepository.Set(siteId, pageId, nameof(Page), action.ToString(), roleIds, cancellationToken);
    }

    public async Task<IEnumerable<Permission>> Get(Guid siteId, SitePermissionAction action, CancellationToken cancellationToken = default)
    {
        return await permissionRepository.Get(siteId, siteId, nameof(Site), action.ToString(), cancellationToken);
    }

    public async Task<IEnumerable<Permission>> Get(Guid siteId, Guid pageId, PagePermissionAction action, CancellationToken cancellationToken = default)
    {
        return await permissionRepository.Get(siteId, pageId, nameof(Page), action.ToString(), cancellationToken);
    }
}
