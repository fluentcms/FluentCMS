namespace FluentCMS.Core.Api.Common;

// The codes of If-Match, which carries the version a write was based on.
internal static class PreconditionErrorCodes
{
    public const string IfMatchRequired = "FCMS3130";
    public const string IfMatchInvalid = "FCMS3131";
}
