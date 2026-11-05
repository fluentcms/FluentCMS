namespace FluentCMS.Core.Api.Common;

// The codes of the request protections: rate limits and request size.
internal static class LimitErrorCodes
{
    public const string RateLimited = "FCMS3110";
    public const string RequestTooLarge = "FCMS3111";
}
