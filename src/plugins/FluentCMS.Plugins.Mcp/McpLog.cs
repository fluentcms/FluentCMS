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
}
