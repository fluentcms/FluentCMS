namespace FluentCMS.Core.Api.Common;

// The codes of the request bridge, which resolves the site and locale of a request before the engine sees it.
internal static class RequestErrorCodes
{
    public const string SiteOutsideBinding = "FCMS3121";
    public const string SiteNotNamed = "FCMS3122";
}
