using FluentCMS.Content;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Mcp;

// What stands between an agent and a mistake (spec 5.4, "Safety"): the dry runs and their confirmation tokens, the cap of
// the bulk tool, the tool calls a client may make in a minute, and the sizes of a request and of a list.
internal static class McpSafety
{
    public static void Add(IPluginServiceBuilder plugin)
    {
        plugin.Services.AddOptions<McpOptions>().Validate(
            options => options.SafetyIsValid(),
            $"{McpOptions.Section}: MaxBulkItems is from 1 to {BulkRequest.MaxItems}, MaxRequestBytes and MaxResultBytes are {McpOptions.MinBytes} or more, MaxListTextLength is {McpOptions.MinListTextLength} or more, and Limits:WritesPerMinute is from 1 to Limits:CallsPerMinute.");
        plugin.Services.AddSingleton<McpConfirmations>();
        plugin.Services.AddSingleton<McpCallLimits>();
        plugin.Services.AddScoped<McpDryRuns>();

        plugin
            .AddErrorCode(McpErrorCodes.ConfirmationRequired, 409, "Confirmation required")
            .AddErrorCode(McpErrorCodes.ConfirmationInvalid, 409, "Confirmation token not of this call")
            .AddErrorCode(McpErrorCodes.ConfirmationExpired, 409, "Confirmation token expired")
            .AddErrorCode(McpErrorCodes.PlanChanged, 409, "Plan changed since the dry run")
            .AddErrorCode(McpErrorCodes.CallLimitExceeded, 429, "Too many tool calls")
            .AddErrorCode(McpErrorCodes.BulkLimitExceeded, 422, "Bulk limit exceeded");
    }
}
