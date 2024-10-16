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
        if (viewState.Site.HasAdminAccess)
            return true;

        // site contributors have always access to all areas
        if (viewState.Site.HasContributorAccess)
            return true;

        return viewState.Page.HasAdminAccess;
    }

    public static bool HasPageContributorAccess(this ViewState viewState)
    {
        if (viewState.Page.Locked)
            return false;

        // Admin roles should always have contributor access
        if (viewState.HasPageAdminAccess())
            return true;

        return viewState.Page.HasContributorAccess;
    }

    public static bool HasPageViewAccess(this ViewState viewState)
    {
        // Admin and contributor roles should always have view access
        if (viewState.HasPageContributorAccess() || viewState.HasPageAdminAccess())
            return true;

        return viewState.Page.HasViewAccess;
    }
}
