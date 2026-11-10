namespace FluentCMS.Plugins.Mcp;

// The MCP plugin has the codes from FCMS1600 up to 1639. Each is added with AddErrorCode in McpPlugin.
internal static class McpErrorCodes
{
    public const string UnknownTool = "FCMS1600";
    public const string UnknownResource = "FCMS1601";
    public const string UnknownPrompt = "FCMS1602";

    // FCMS1612 to FCMS1619: the content and media tools.
    public const string TypeNotAvailable = "FCMS1612";
    public const string SingletonEmpty = "FCMS1613";
    public const string UploadSourceRefused = "FCMS1614";
    public const string UploadSourceFailed = "FCMS1615";
}
