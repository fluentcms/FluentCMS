namespace FluentCMS.Services.Permissions;

public interface IPermissionManager
{
    Task<bool> HasAccess(GlobalPermissionAction action, CancellationToken cancellationToken = default);
    Task<bool> HasAccess(Guid siteId, SitePermissionAction action, CancellationToken cancellationToken = default);
    Task<IEnumerable<Site>> GetAccessible(IEnumerable<Site> sites, SitePermissionAction action, CancellationToken cancellationToken = default);
}

public enum GlobalPermissionAction
{
    SuperAdmin
}

public enum SitePermissionAction
{
    SiteContributor,
    SiteAdmin
}

public enum PagePermissionAction
{
    PageView,
    PageContributor,
    PageAdmin
}
