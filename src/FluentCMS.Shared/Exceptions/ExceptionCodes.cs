namespace FluentCMS;

public class ExceptionCodes
{
    #region General

    public const string GeneralPermissionDenied = "General.PermissionDenied";

    #endregion

    #region Host

    public const string HostNotFound = "Host.NotFound";
    public const string HostAlreadyInitialized = "Host.AlreadyInitialized";
    public const string HostUnableToCreate = "Host.UnableToCreate";
    public const string HostUnableToUpdate = "Host.UnableToUpdate";
    public const string HostUnableToDelete = "Host.UnableToDelete";
    public const string HostAtLeastOneSuperUser = "Host.AtLeastOneSuperUser";
    public const string HostUnableToRemoveYourself = "Host.UnableToRemoveYourself";

    #endregion

    #region User

    public const string UserNotFound = "User.NotFound";
    public const string UserLoginFailed = "User.LoginFailed";
    public const string UserChangePasswordFailed = "User.ChangePasswordFailed";
    public const string UserTokenGenerationFailed = "User.TokenGenerationFailed";

    #endregion

    #region Page

    public const string PageUnableToCreate = "Page.UnableToCreated";
    public const string PageUnableToUpdate = "Page.UnableToUpdate";
    public const string PageUnableToDelete = "Page.UnableToDelete";
    public const string PagePathMustBeUnique = "Page.PathMustBeUnique";
    public const string PageNotFound = "Page.NotFound";
    public const string PageParentPageNotFound = "Page.ParentPageNotFound";
    public const string PageParentMustBeOnTheSameSite = "Page.ParentMustBeOnTheSameSite";
    public const string PageViewPermissionsAreNotASubsetOfParent = "Page.ViewPermissionsAreNotASubsetOfParent";
    public const string PageSiteIdCannotBeChanged = "Page.SiteIdCannotBeChanged";
    public const string PageHasChildren = "Page.PageHasChildren";


    #endregion

    #region Site

    public const string SiteNotFound = "Site.NotFound";

    #endregion

    #region Role


    public const string RoleUnableToCreate = "Role.UnableToCreated";
    public const string RoleUnableToUpdate = "Role.UnableToUpdate";
    public const string RoleUnableToDelete = "Role.UnableToDelete";
    public const string RoleNotFound = "Role.NotFound";
    public const string RoleNameMustBeUnique = "Role.NameMustBeUnique";

    #endregion
}
