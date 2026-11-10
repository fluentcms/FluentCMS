namespace FluentCMS.Plugins.Mcp;

// The MCP plugin has the codes from FCMS1600 up to 1639. Each is added with AddErrorCode in McpPlugin.
internal static class McpErrorCodes
{
    public const string UnknownTool = "FCMS1600";
    public const string UnknownResource = "FCMS1601";
    public const string UnknownPrompt = "FCMS1602";

    // FCMS1603 to 1611: the MCP clients of the Management API. Added in McpClientSetup.
    public const string ClientNotFound = "FCMS1603";
    public const string ClientNotPending = "FCMS1604";
    public const string ClientRoleRequired = "FCMS1605";
    public const string ClientRoleUnknown = "FCMS1606";
}
