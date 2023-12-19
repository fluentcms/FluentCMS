namespace FluentCMS;

public class ExceptionCodes
{
    #region SystemSettings

    public const string SystemSettingsNotFound = "SystemSettings.NotFound";
    public const string SystemSettingsAlreadyInitialized = "SystemSettings.AlreadyInitialized";
    public const string SystemSettingsUnableToCreate = "SystemSettings.UnableToCreate";
    public const string SystemSettingsUnableToUpdate = "SystemSettings.UnableToUpdate";
    public const string SystemSettingsUnableToDelete = "SystemSettings.UnableToDelete";
    public const string SystemSettingsAtLeastOneSuperUser = "SystemSettings.AtLeastOneSuperUser";
    public const string SystemSettingsUnableToRemoveYourself = "SystemSettings.UnableToRemoveYourself";

    #endregion

    #region User

    public const string UserNotFound = "User.NotFound";
    public const string UserLoginFailed = "User.LoginFailed";
    public const string UserChangePasswordFailed = "User.ChangePasswordFailed";
    public const string UserTokenGenerationFailed = "User.TokenGenerationFailed";

    #endregion

    #region App

    public const string AppNotFound = "App.NotFound";
    public const string AppUrlMustBeUnique = "App.UrlMustBeUnique";
    public const string AppUnableToCreate = "App.UnableToCreated";
    public const string AppUnableToUpdate = "App.UnableToUpdate";
    public const string AppUnableToDelete = "App.UnableToDelete";

    #endregion

    #region Role


    public const string RoleUnableToCreate = "Role.UnableToCreated";
    public const string RoleUnableToUpdate = "Role.UnableToUpdate";
    public const string RoleUnableToDelete = "Role.UnableToDelete";
    public const string RoleNotFound = "Role.NotFound";
    public const string RoleNameMustBeUnique = "Role.NameMustBeUnique";

    #endregion

    #region Content
    public const string ContentUnableToCreate = "Content.UnableToCreated";
    public const string ContentUnableToUpdate = "Content.UnableToUpdate";
    public const string ContentUnableToDelete = "Content.UnableToDelete";
    public const string ContentNotFound = "Content.NotFound";
    public const string ContentTypeMismatch = "Content.TypeMismatch";
    public const string ContentAppIdMismatch = "Content.AppIdMismatch";
    #endregion

    #region ContentType

    public const string ContentTypeUnableToCreate = "ContentType.UnableToCreated";
    public const string ContentTypeUnableToUpdate = "ContentType.UnableToUpdate";
    public const string ContentTypeUnableToDelete = "ContentType.UnableToDelete";
    public const string ContentTypeNotFound = "ContentType.NotFound";
    public const string ContentTypeNameCannotBeChanged = "ContentType.NameCannotBeChanged";
    public const string ContentTypeNameMustBeUnique = "ContentType.NameMustBeUnique";
    public const string ContentTypeFieldNotFound = "ContentType.FieldNotFound";

    #endregion
}
