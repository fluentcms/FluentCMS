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
}
