namespace FluentCMS.Core.Api.Common;

// The codes the HTTP layer raises on its own; the engine's codes are in the error code classes of each area.
internal static class ApiErrorCodes
{
    public const string ValidationFailed = "FCMS2100";
    public const string BadRequest = "FCMS3101";
    public const string Unauthorized = "FCMS3102";
    public const string Forbidden = "FCMS3103";
    public const string NotFound = "FCMS3104";
    public const string VersionConflict = "FCMS3105";
    public const string InternalError = "FCMS3199";
}
