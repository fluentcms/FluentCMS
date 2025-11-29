namespace FluentCMS.Api.Plugins.CmsCoreManagement;

public static class MessageCodes
{
    #region Site

    public const string SiteUrlCannotBeEmpty = "Site.UrlCannotBeEmpty";
    public const string SiteUrlIsInvalid = "Site.UrlIsInvalid";
    public const string SiteUrlMustBeUnique = "Site.UrlMustBeUnique";
    public const string SiteFailedToAdd = "Site.FailedToAdd";
    public const string SiteFailedToUpdate = "Site.FailedToUpdate";
    public const string SiteFailedToRemove = "Site.FailedToRemove";

    #endregion

    #region Page

    public const string PageUrlCannotBeEmpty = "Page.UrlCannotBeEmpty";
    public const string PageUrlIsInvalid = "Page.UrlIsInvalid";
    public const string PageUrlMustBeUnique = "Page.UrlMustBeUnique";
    public const string PageFailedToAdd = "Page.FailedToAdd";
    public const string PageFailedToUpdate = "Page.FailedToUpdate";
    public const string PageFailedToRemove = "Page.FailedToRemove";

    #endregion

    #region Folder
    public const string FolderInvalidName = "Folder.InvalidName";
    public const string FolderAlreadyExists = "Folder.AlreadyExists";    
    public const string FolderUnableToCreate = "Folder.UnableToCreate";
    public const string FolderParentNotFound = "Folder.ParentNotFound";
    public const string FolderNotFound = "Folder.NotFound";

    public const string FolderCannotRenameRootFolder = "Folder.CannotRenameRootFolder";
    public const string FolderUnableToUpdate = "Folder.UnableToUpdate";

    public const string FolderCannotMoveToItself = "Folder.CannotMoveToItself";
    public const string FolderCannotMoveRootFolder = "Folder.CannotMoveRootFolder";
    public const string FolderCannotMoveToChild = "Folder.CannotMoveToChild";


    #endregion

    #region File

    public const string FileUnableToDelete = "File.UnableToDelete";
    public const string FileNotFound = "File.NotFound";
    public const string FileInvalidName = "File.InvalidName";
    public const string FileAlreadyExists = "File.AlreadyExists";
    public const string FileUnableToUpdate = "File.UnableToUpdate";

    #endregion
    
}
