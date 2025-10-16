namespace FluentCMS.Api.Core.Services;

public static class ExceptionCodes
{
    #region API Token

    public const string ApiTokenExpired = "ApiToken.Expired";
    public const string ApiTokenInactive = "ApiToken.Inactive";
    public const string ApiTokenInvalid = "ApiToken.Invalid";
    public const string ApiTokenInvalidSecret = "ApiToken.InvalidSecret";
    public const string ApiTokenNameIsDuplicated = "ApiToken.NameIsDuplicated";

    #endregion

    #region User

    public const string UserLoginFailed = "User.LoginFailed";
    public const string UserChangePasswordFailed = "User.ChangePasswordFailed";
    public const string UserTokenGenerationFailed = "User.TokenGenerationFailed";
    public const string UserSuperAdminCanNotBeDeleted = "User.SuperAdminCanNotBeDeleted";
    public const string UserInvalidPassword = "User.InvalidPassword";
    public const string UserInvalidUsername = "User.InvalidUsername";

    #endregion

    #region File

    public const string FileNotFound = "File.NotFound";
    public const string FileInvalidName = "File.InvalidName";
    public const string FileFailedToRemove = "File.FailedToRemove";
    public const string FileAlreadyExists = "File.AlreadyExists";
    public const string FileFailedToToAdd = "File.FileFailedToToAdd";
    public const string FileFailedToUpdate = "File.FileFailedToUpdate";

    #endregion



    #region Setup

    public const string SetupAlreadyInitialized = "Setup.AlreadyInitialized";

    #endregion

    #region GlobalSettings

    public const string GlobalSettingsNotFound = "GlobalSettings.NotFound";
    public const string GlobalSettingsSuperAdminCanNotBeDeleted = "GlobalSettings.SuperAdminCanNotBeDeleted";
    public const string GlobalSettingsSuperAdminAtLeastOne = "GlobalSettings.SuperAdminAtLeastOne";

    #endregion

    #region App

    public const string AppNotFound = "App.NotFound";
    public const string AppSlugNotUnique = "App.SlugNotUnique";
    public const string AppUnableToCreate = "App.UnableToCreate";
    public const string AppUnableToUpdate = "App.UnableToUpdate";
    public const string AppUnableToDelete = "App.UnableToDelete";

    #endregion

    #region Role

    public const string RoleNameShouldBeUnique = "Role.NameShouldBeUnique";
    public const string RoleTypeCanNotBeChanged = "Role.TypeCanNotBeChanged";
    public const string RoleDefaultCanNotBeDeleted = "Role.DefaultCanNotBeDeleted";

    #endregion

    #region Plugin

    public const string PluginUnableToCreate = "Plugin.UnableToCreate";
    public const string PluginUnableToUpdate = "Plugin.UnableToUpdate";
    public const string PluginUnableToDelete = "Plugin.UnableToDelete";
    public const string PluginNotFound = "Plugin.NotFound";
    public const string PluginUnableToUpdateCols = "Plugin.UnableToUpdateCols";
    public const string PluginUnableToUpdateSettings = "Plugin.UnableToUpdateSettings";

    #endregion

    #region PluginDefinition

    public const string PluginDefinitionUnableToCreate = "PluginDefinition.UnableToCreate";
    public const string PluginDefinitionUnableToUpdate = "PluginDefinition.UnableToUpdate";
    public const string PluginDefinitionUnableToDelete = "PluginDefinition.UnableToDelete";
    public const string PluginDefinitionNotFound = "PluginDefinition.NotFound";

    #endregion

    #region Folder

    public const string FolderUnableToCreate = "Folder.UnableToCreate";
    public const string FolderInvalidName = "Folder.InvalidName";
    public const string FolderAlreadyExists = "Folder.AlreadyExists";
    public const string FolderParentNotFound = "Folder.ParentNotFound";
    public const string FolderCannotRenameRootFolder = "Folder.CannotRenameRootFolder";
    public const string FolderCannotMoveRootFolder = "Folder.CannotMoveRootFolder";
    public const string FolderCannotMoveToItself = "Folder.CannotMoveToItself";
    public const string FolderCannotMoveToChild = "Folder.CannotMoveToChild";
    public const string FolderNotFound = "Folder.NotFound";
    public const string FolderUnableToUpdate = "Folder.UnableToUpdate";
    public const string FolderUnableToDelete = "Folder.UnableToDelete";

    #endregion

    #region Layout

    public const string LayoutUnableToDeleteDefaultLayout = "Layout.UnableToDeleteDefaultLayout";

    #endregion

    #region Site

    public const string SiteUrlIsEmpty = "Site.UrlIsEmpty";
    public const string SiteUrlIsInvalid = "Site.UrlIsInvalid";
    public const string SiteUrlMustBeUnique = "Site.UrlMustBeUnique";
    public const string SiteUnableToCreate = "Site.UnableToCreate";
    public const string SiteUnableToUpdate = "Site.UnableToUpdate";
    public const string SiteUnableToDelete = "Site.UnableToDelete";

    #endregion

    #region Page

    public const string PageUnableToCreate = "Page.UnableToCreate";
    public const string PageUnableToUpdate = "Page.UnableToUpdate";
    public const string PageUnableToDelete = "Page.UnableToDelete";
    public const string PagePathMustBeUnique = "Page.PathMustBeUnique";
    public const string PageNotFound = "Page.NotFound";
    public const string PageCannotDeleteHome = "Page.CannotDeleteHome";
    public const string PagePathInvalidCharacter = "Page.PathInvalidCharacter";
    public const string PageParentPageNotFound = "Page.ParentPageNotFound";
    public const string PageParentCannotBeHome = "Page.ParentCannotBeHome";
    public const string PagePathReservedName = "Page.PathReservedName";
    public const string PageHomeCannotHaveParent = "Page.HomeCannotHaveParent";
    public const string PageUnableToUpdateHome = "Page.UnableToUpdateHome";
    public const string PageParentMustBeOnTheSameSite = "Page.ParentMustBeOnTheSameSite";
    public const string PageHasChildren = "Page.PageHasChildren";

    #endregion

    #region Permission

    public const string PermissionUnableToCreate = "Permission.UnableToCreate";
    public const string PermissionUnableToUpdate = "Permission.UnableToUpdate";
    public const string PermissionUnableToDelete = "Permission.UnableToDelete";
    public const string PermissionNotFound = "Permission.NotFound";
    public const string PermissionDenied = "Permission.Denied";

    #endregion
}
