namespace FluentCMS.Plugins.Mcp;

// The MCP plugin has the codes from FCMS1600 up to 1639. Each is added with AddErrorCode in McpPlugin.
internal static class McpErrorCodes
{
    public const string UnknownTool = "FCMS1600";
    public const string UnknownResource = "FCMS1601";
    public const string UnknownPrompt = "FCMS1602";

    // FCMS1620 to 1629: the agent instructions and the stored prompts of a site.
    public const string InvalidInstruction = "FCMS1620";
    public const string InstructionTypeNotFound = "FCMS1621";
    public const string InstructionNotFound = "FCMS1622";
    public const string InvalidPrompt = "FCMS1623";
    public const string PromptNotFound = "FCMS1624";
    public const string PromptNameTaken = "FCMS1625";
    public const string PromptNameReserved = "FCMS1626";
    public const string UndeclaredPromptArgument = "FCMS1627";
    public const string MissingPromptArgument = "FCMS1628";
}
