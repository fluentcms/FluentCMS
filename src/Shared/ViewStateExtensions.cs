namespace FluentCMS;

public static class ViewStateExtensions
{
    public static bool HasPageAdminAccess(this ViewState viewState)
    {
        if (viewState.Page.Locked)
            return false;

        // super admins have always access to all areas
        if (viewState.User.IsSuperAdmin)
            return true;

        // site admins have always access to all areas
        if (viewState.Site.AdminRoles.Any(role =>
            viewState.User?.Roles.Select(x => x.Id).Contains(role.Id) ?? false))
            return true;

        // site contributors have always access to all areas
        if (viewState.Site.ContributorRoles.Any(role =>
            viewState.User?.Roles.Select(x => x.Id).Contains(role.Id) ?? false))
            return true;

        return viewState.Page.AdminRoleIds.Any(roleId =>
                viewState.User?.Roles.Select(x => x.Id).Contains(roleId) ?? false);
    }

    public static bool HasPageContributorAccess(this ViewState viewState)
    {
        if (viewState.Page.Locked)
            return false;

        // Admin roles should always have contributor access
        if (viewState.HasPageAdminAccess())
            return true;

        return viewState.Page.ContributorRoleIds.Any(roleId =>
                viewState.User?.Roles.Select(x => x.Id).Contains(roleId) ?? false);
    }

    public static bool HasPageViewAccess(this ViewState viewState)
    {
        // Admin and contributor roles should always have view access
        if (viewState.HasPageContributorAccess() || viewState.HasPageAdminAccess())
            return true;

        return viewState.Page.ViewRoleIds.Any(roleId =>
                viewState.User?.Roles.Select(x => x.Id).Contains(roleId) ?? false);
    }
}
