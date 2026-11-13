namespace FluentCMS.Plugins.AiAssist;

// The AI assist plugin has the codes from FCMS1680 up to 1699. Each is added with AddErrorCode in AiAssistPlugin.
internal static class AiErrorCodes
{
    public const string InvalidRequest = "FCMS1680";
    public const string UnknownAction = "FCMS1681";
    public const string NoProvider = "FCMS1682";
    public const string FieldNotOptedIn = "FCMS1683";
    public const string EndpointNotAllowed = "FCMS1684";
    public const string ProviderNotConfigured = "FCMS1685";
    public const string InputTooLong = "FCMS1686";
    public const string CallLimitExceeded = "FCMS1687";
    public const string TimedOut = "FCMS1688";
    public const string ProviderFailed = "FCMS1689";
    public const string AnswerUnusable = "FCMS1690";
    public const string ImageNotAvailable = "FCMS1691";
    public const string ProviderRemoved = "FCMS1692";
}
