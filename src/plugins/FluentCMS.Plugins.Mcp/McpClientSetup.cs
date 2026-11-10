using FluentCMS.Agents;
using FluentCMS.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Mcp;

// What the plugin adds for the MCP clients that register themselves (RFC 7591): the settings area with the registration
// policy, which core's /connect/register asks, the service behind the Management endpoints, the cleanup of clients nobody
// approved, and the codes FCMS1603 to FCMS1606.
internal static class McpClientSetup
{
    public static void Add(IPluginServiceBuilder plugin)
    {
        plugin.Services.AddScoped<IMcpClientRegistrationPolicy, McpClientRegistrationPolicySource>();
        plugin.Services.AddScoped<McpClientService>();
        plugin.Services.AddSingleton<IInsecureDefaultSource, McpInsecureDefaults>();
        plugin.Services.AddHostedService<McpClientCleanupSchedule>();

        plugin
            .AddSettings<McpSettings>()
            .AddJob<McpClientCleanupJob, McpClientCleanupPayload>(McpClientCleanupJob.Type)
            .AddErrorCode(McpErrorCodes.ClientNotFound, 404, "MCP client not found")
            .AddErrorCode(McpErrorCodes.ClientNotPending, 409, "MCP client is not pending")
            .AddErrorCode(McpErrorCodes.ClientRoleRequired, 422, "MCP client role missing")
            .AddErrorCode(McpErrorCodes.ClientRoleUnknown, 422, "Unknown role for an MCP client");
    }
}
