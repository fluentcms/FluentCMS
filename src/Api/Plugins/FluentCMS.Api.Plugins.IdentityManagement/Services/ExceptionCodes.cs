namespace FluentCMS.Api.Plugins.IdentityManagement.Services;

public static class ExceptionCodes
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

}
