namespace FluentCMS.Core.Identity.Members;

internal static class MemberErrorCodes
{
    public const string InvalidOptions = "FCMS3160";
    public const string MemberNotFound = "FCMS3161";
    public const string InvalidMember = "FCMS3162";
    public const string EmailTaken = "FCMS3163";
    public const string InvalidEmail = "FCMS3164";
    public const string InvalidPassword = "FCMS3165";
    public const string InvalidToken = "FCMS3166";
    public const string WrongPassword = "FCMS3167";
    public const string GroupNotFound = "FCMS3168";
    public const string InvalidGroup = "FCMS3169";
    public const string GroupNameTaken = "FCMS3170";
    public const string AlreadyInGroup = "FCMS3171";
    public const string NotInGroup = "FCMS3172";
    public const string NoMailLink = "FCMS3173";
    public const string MemberChanged = "FCMS3174";

    // 3180 to 3189: the external providers of the member pool.
    public const string InvalidProvider = "FCMS3180";
    public const string ProviderNoEmail = "FCMS3181";
    public const string ProviderEmailTaken = "FCMS3182";
    public const string ProviderMemberNotCreated = "FCMS3183";
    public const string LastWayIn = "FCMS3184";
    public const string LoginNotFound = "FCMS3185";
    public const string LoginTaken = "FCMS3186";
    public const string ProviderRegistrationClosed = "FCMS3187";
}
