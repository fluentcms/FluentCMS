using FluentCMS.Authorization;

namespace FluentCMS.Plugins.Search;

public sealed class SearchPermissions : IPermissionProvider
{
    // Rebuilding the index of a site.
    public const string Manage = "search.manage";

    public IEnumerable<Permission> GetPermissions() => [new Permission(Manage, "Rebuild the search index of a site.")];
}
