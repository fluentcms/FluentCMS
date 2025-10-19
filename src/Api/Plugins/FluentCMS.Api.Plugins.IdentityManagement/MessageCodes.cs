namespace FluentCMS.Api.Plugins.IdentityManagement;

public static class MessageCodes
{
    #region User

    public const string UserLoginFailed = "User.LoginFailed";
    public const string UserChangePasswordFailed = "User.ChangePasswordFailed";
    public const string UserTokenGenerationFailed = "User.TokenGenerationFailed";
    public const string UserSuperAdminCanNotBeDeleted = "User.SuperAdminCanNotBeDeleted";
    public const string UserInvalidPassword = "User.InvalidPassword";
    public const string UserInvalidUsername = "User.InvalidUsername";

    #endregion

    #region Role

    public const string RoleNameShouldBeUnique = "Role.NameShouldBeUnique";
    public const string RoleTypeCanNotBeChanged = "Role.TypeCanNotBeChanged";
    public const string RoleDefaultCanNotBeDeleted = "Role.DefaultCanNotBeDeleted";

    #endregion

    #region Account

    public const string AccountEmailAlreadyExists = "Account.EmailAlreadyExists";
    public const string AccountLoginFailed = "User.AccountLoginFailed";
    public const string AccountEmailConfirmedFailed = "Account.EmailConfirmedFailed";
    public const string AccountChangePasswordFailed = "Account.ChangePasswordFailed";
    public const string AccountPasswordChangedSuccessfully = "Account.PasswordChangedSuccessfully";
    public const string AccountResetPasswordFailed = "Account.ResetPasswordFailed";
    public const string AccountRegisteredSuccessfully = "Account.RegisteredSuccessfully";
    public const string AccountLoginSuccessful = "Account.LoginSuccessful";
    public const string AccountLogoutSuccessful = "Account.LogoutSuccessful";
    public const string AccountEmailConfirmedSuccessfully = "Account.EmailConfirmedSuccessfully";
    public const string AccountConfirmationEmailSent = "Account.ConfirmationEmailSent";
    public const string AccountPasswordResetEmailSent = "Account.PasswordResetEmailSent";
    public const string AccountPasswordResetSuccessfully = "Account.PasswordResetSuccessfully";

    #endregion

}
