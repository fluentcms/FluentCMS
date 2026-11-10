namespace FluentCMS.Plugins.Mcp;

// The MCP plugin has the codes from FCMS1600 up to 1639. Each is added with AddErrorCode in McpPlugin.
internal static class McpErrorCodes
{
    public const string UnknownTool = "FCMS1600";
    public const string UnknownResource = "FCMS1601";
    public const string UnknownPrompt = "FCMS1602";

    // FCMS1612 to 1619: the content and media tools.
    public const string TypeNotAvailable = "FCMS1612";
    public const string SingletonEmpty = "FCMS1613";
    public const string UploadSourceRefused = "FCMS1614";
    public const string UploadSourceFailed = "FCMS1615";

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

    // FCMS1630 to 1639: the dry runs, the bulk tool and the call limits.
    public const string ConfirmationRequired = "FCMS1630";
    public const string ConfirmationInvalid = "FCMS1631";
    public const string ConfirmationExpired = "FCMS1632";
    public const string PlanChanged = "FCMS1633";
    public const string CallLimitExceeded = "FCMS1634";
    public const string BulkLimitExceeded = "FCMS1635";
}
