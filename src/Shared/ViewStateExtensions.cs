namespace FluentCMS;

public static class ViewStateExtensions
{
    public static bool HasPageAdminAccess(this ViewState viewState)
    {
        if (viewState.Page.Locked)
            return false;

        return viewState.User?.Roles?.Any(role => role.Type == RoleTypesViewState.Authenticated) ?? false;
    }

    public static bool HasPageContributorAccess(this ViewState viewState)
    {
        if (viewState.Page.Locked)
            return false;

        return viewState.User?.Roles?.Any(role => role.Type == RoleTypesViewState.Authenticated) ?? false;
    }
}
