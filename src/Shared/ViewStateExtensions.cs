namespace FluentCMS;

public static class ViewStateExtensions
{
    public static bool HasPageAdminAccess(this ViewState viewState)
    {
        return viewState.User.Roles.Any(role => role.Type == RoleTypesViewState.Authenticated);
    }

    public static bool HasPageContributorAccess(this ViewState viewState)
    {
        return viewState.User.Roles.Any(role => role.Type == RoleTypesViewState.Authenticated);
    }
}
