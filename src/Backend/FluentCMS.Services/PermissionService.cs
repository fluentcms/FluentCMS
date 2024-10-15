namespace FluentCMS.Services;

public interface IPermissionService : IAutoRegisterService
{
    Task<IEnumerable<Permission>> Set(Guid siteId, SitePermissionAction action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<Permission>> Set(Guid siteId, Guid pageId, PagePermissionAction action, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
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
}
