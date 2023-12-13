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
    public const string SiteUrlMustBeUnique = "Site.UrlMustBeUnique";
    public const string SiteUnableToCreate = "Site.UnableToCreated";
    public const string SiteUnableToUpdate = "Site.UnableToUpdate";
    public const string SiteUnableToDelete = "Site.UnableToDelete";

    #endregion

    #region Role


    public const string RoleUnableToCreate = "Role.UnableToCreated";
    public const string RoleUnableToUpdate = "Role.UnableToUpdate";
    public const string RoleUnableToDelete = "Role.UnableToDelete";
    public const string RoleNotFound = "Role.NotFound";
    public const string RoleNameMustBeUnique = "Role.NameMustBeUnique";

    #endregion

    #region Permission

    public const string PermissionUnableToCreate = "Permission.UnableToCreated";

    #endregion

    #region Plugin

    public const string PluginUnableToCreate = "Plugin.UnableToCreated";
    public const string PluginUnableToUpdate = "Plugin.UnableToUpdate";
    public const string PluginUnableToDelete = "Plugin.UnableToDelete";
    public const string PluginNotFound = "Plugin.NotFound";

    #endregion

    #region PluginDefinition

    public const string PluginDefinitionUnableToCreate = "PluginDefinition.UnableToCreated";
    public const string PluginDefinitionUnableToUpdate = "PluginDefinition.UnableToUpdate";
    public const string PluginDefinitionUnableToDelete = "PluginDefinition.UnableToDelete";
    public const string PluginDefinitionNotFound = "PluginDefinition.NotFound";

    #endregion

    #region Layout

    public const string LayoutUnableToCreate = "Layout.UnableToCreated";
    public const string LayoutUnableToUpdate = "Layout.UnableToUpdate";
    public const string LayoutUnableToDelete = "Layout.UnableToDelete";
    public const string LayoutNotFound = "Layout.NotFound";

    #endregion

    #region Content
    public const string ContentUnableToCreate = "Content.UnableToCreated";
    public const string ContentUnableToUpdate = "Content.UnableToUpdate";
    public const string ContentUnableToDelete = "Content.UnableToDelete";
    public const string ContentNotFound = "Content.NotFound";
    public const string ContentTypeMismatch = "Content.TypeMismatch";
    public const string ContentSiteIdMismatch = "Content.SiteIdMismatch";
    public const string ContentPluginIdMismatch = "Content.ContentPluginIdMismatch";
    #endregion

    #region ContentType

    public const string ContentTypeUnableToCreate = "ContentType.UnableToCreated";
    public const string ContentTypeUnableToUpdate = "ContentType.UnableToUpdate";
    public const string ContentTypeUnableToDelete = "ContentType.UnableToDelete";
    public const string ContentTypeNotFound = "ContentType.NotFound";
    public const string ContentTypeNameCannotBeChanged = "ContentType.NameCannotBeChanged";
    public const string ContentTypeNameMustBeUnique = "ContentType.NameMustBeUnique";

    #endregion
}
