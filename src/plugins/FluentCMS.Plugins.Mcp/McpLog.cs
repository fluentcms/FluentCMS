using FluentCMS.Content;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Mcp;

// Event ids 9280 to 9299. A line names the tool, the client and the principal, never an argument or a result.
internal static partial class McpLog
{
    [LoggerMessage(EventId = 9280, Level = LogLevel.Information, Message = "MCP tool {Tool} called by {PrincipalKind} {PrincipalId} through client {ClientId} in site {SiteId}: {Outcome} {Code} in {DurationMs:0} ms.")]
    public static partial void ToolCalled(ILogger logger, string tool, PrincipalKind principalKind, Guid principalId, string? clientId, Guid siteId, string outcome, string? code, double durationMs);

    [LoggerMessage(EventId = 9281, Level = LogLevel.Error, Message = "MCP {Request} of {Name} failed with an exception that says nothing of its cause, answered as {Code}.")]
    public static partial void RequestFailed(ILogger logger, Exception exception, string request, string name, string code);

    [LoggerMessage(EventId = 9282, Level = LogLevel.Error, Message = "The MCP provider {Provider} of the plugin {Plugin} failed to list, so what it offers is left out of this answer.")]
    public static partial void ListFailed(ILogger logger, Exception exception, string provider, string plugin);

    [LoggerMessage(EventId = 9283, Level = LogLevel.Warning, Message = "The MCP provider {Provider} of the plugin {Plugin} lists {Name}, which is left out: {Reason}")]
    public static partial void Skipped(ILogger logger, string provider, string plugin, string name, string reason);

    // 9284 to 9287: the MCP clients that register themselves.
    [LoggerMessage(EventId = 9284, Level = LogLevel.Information, Message = "Removed {Count} MCP clients that registered themselves and were not approved within {Days:0} days.")]
    public static partial void UnapprovedClientsRemoved(ILogger logger, int count, double days);

    [LoggerMessage(EventId = 9285, Level = LogLevel.Error, Message = "Could not register the recurring cleanup of MCP clients that were not approved; they stay until it is registered.")]
    public static partial void ClientCleanupNotScheduled(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9288, Level = LogLevel.Warning, Message = "The content type {Type} gets no MCP tools of its own, since more types are exposed than FluentCms:Mcp:MaxExposedTypes ({Max}) allows; it is reached through the query tool.")]
    public static partial void TypeOverCap(ILogger logger, string type, int max);

    [LoggerMessage(EventId = 9289, Level = LogLevel.Warning, Message = "MCP media_upload did not fetch from the host {Host}: {Reason}")]
    public static partial void UploadSourceRefused(ILogger logger, string host, string reason);

    // 9291 to 9294: the agent instructions and the stored prompts. A line names what changed and never holds its text.
    [LoggerMessage(EventId = 9291, Level = LogLevel.Information, Message = "The agent instructions of {Type} were {Change} in site {SiteId}; no type is the guidelines of the site.")]
    public static partial void InstructionChanged(ILogger logger, string change, string? type, Guid siteId);

    [LoggerMessage(EventId = 9292, Level = LogLevel.Information, Message = "The stored MCP prompt {Name} was {Change} in site {SiteId}.")]
    public static partial void PromptChanged(ILogger logger, string change, string name, Guid siteId);

    [LoggerMessage(EventId = 9293, Level = LogLevel.Warning, Message = "The stored MCP prompt {Name} of site {SiteId} holds arguments that cannot be read, so it is left out until it is saved again.")]
    public static partial void PromptUnreadable(ILogger logger, string name, Guid siteId);

    // 9295 to 9299: the call limits and the dry runs.
    [LoggerMessage(EventId = 9295, Level = LogLevel.Warning, Message = "The MCP client {ClientId} of {PrincipalKind} {PrincipalId} is over its {Limit} {Kind} a minute in site {SiteId}, so its calls are refused for {RetryAfterSeconds} s. Its next refusals are not logged for a minute.")]
    public static partial void CallLimitExceeded(ILogger logger, string? clientId, PrincipalKind principalKind, Guid principalId, int limit, string kind, Guid siteId, int retryAfterSeconds);

    [LoggerMessage(EventId = 9296, Level = LogLevel.Warning, Message = "MCP tool {Tool} did not run for {PrincipalKind} {PrincipalId} through client {ClientId} in site {SiteId}: its confirmation was refused as {Code}.")]
    public static partial void ConfirmationRefused(ILogger logger, string tool, PrincipalKind principalKind, Guid principalId, string? clientId, Guid siteId, string code);
}
