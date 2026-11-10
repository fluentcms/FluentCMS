namespace FluentCMS.Core.Identity.ApiKeys;

internal static class ApiKeyErrorCodes
{
    public const string InvalidName = "FCMS3010";
    public const string UnknownSite = "FCMS3011";
    public const string UnknownType = "FCMS3012";
    public const string InvalidExpiry = "FCMS3013";
    public const string InvalidRateLimit = "FCMS3014";
    public const string NotFound = "FCMS3015";
    public const string InvalidKey = "FCMS3016";
    public const string Expired = "FCMS3017";
    public const string Revoked = "FCMS3018";
    public const string SeveralCredentials = "FCMS3019";
    public const string EmptyBinding = "FCMS3020";
    public const string AddressRefused = "FCMS3023";
    public const string InvalidAddress = "FCMS3024";
    public const string NotRotatable = "FCMS3025";
    public const string InvalidOverlap = "FCMS3026";
}
