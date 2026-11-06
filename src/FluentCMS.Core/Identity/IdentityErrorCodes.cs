namespace FluentCMS.Core.Identity;

internal static class IdentityErrorCodes
{
    public const string InvalidOptions = "FCMS3001";
    public const string InvalidEmail = "FCMS3002";
    public const string EmailTaken = "FCMS3003";
    public const string UnknownRole = "FCMS3004";
    public const string InvalidInvitation = "FCMS3005";
    public const string InvitationExpired = "FCMS3006";
    public const string DeleteNotSupported = "FCMS3007";
    public const string NoEmailSender = "FCMS3008";
    public const string WriteInTransaction = "FCMS3009";
    public const string UserNotFound = "FCMS3046";
    public const string SessionNotFound = "FCMS3047";
    public const string InvitationNotFound = "FCMS3048";
    public const string InvalidUser = "FCMS3049";
    public const string RoleAlreadyHeld = "FCMS3050";
    public const string RoleNotHeld = "FCMS3051";
    public const string TwoFactorEnabled = "FCMS3060";
    public const string TwoFactorNotEnabled = "FCMS3061";
    public const string NoAuthenticator = "FCMS3062";
    public const string InvalidTwoFactorCode = "FCMS3063";
}
