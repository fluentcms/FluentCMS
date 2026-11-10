using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Mcp;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

[assembly: FluentCmsPlugin(typeof(McpPlugin))]

namespace FluentCMS.Plugins.Mcp;

// The MCP server of spec 5.4: Streamable HTTP at /mcp, which serves the tools, resources and prompts that plugins add
// through the contracts of FluentCMS.Agents, as the caller.
[FluentCmsPlugin("mcp", SdkRange = "[1.0.0,2.0.0)")]
public sealed class McpPlugin : IPlugin
{
    public const string Prefix = "/mcp";

    // The scope a bearer token needs at /mcp, which the authorization server of core offers.
    public const string Scope = "mcp";

    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        plugin.Services.AddOptions<McpOptions>()
            .BindConfiguration(McpOptions.Section)
            .Validate(options => options.IsValid(), $"{McpOptions.Section}: Name is 1 to 64 letters, digits, hyphens, underscores and dots, and Title is 1 to 128 characters.")
            .Validate(options => options.ToolsAreValid(), $"{McpOptions.Section}: ExposedTypes holds names of content types, MaxExposedTypes is from 0 to 100 and UploadTimeout is up to 10 minutes.")
            .Validate<SchemaModel>(
                (options, schema) => options.ExposedTypes.All(type => schema.FindContentType(type) is not null),
                $"{McpOptions.Section}: ExposedTypes names a content type the schema does not have.")
            .ValidateOnStart();
        plugin.Services.AddScoped<McpRequests>();
        McpTools.Add(plugin);
        McpServerSetup.Add(plugin.Services);
        McpClientSetup.Add(plugin);
        McpSiteSetup.Add(plugin);
        McpSafety.Add(plugin);

        plugin
            .AddPermissions<McpPermissions>()
            .AddErrorCode(McpErrorCodes.UnknownTool, 404, "Unknown MCP tool")
            .AddErrorCode(McpErrorCodes.UnknownResource, 404, "Unknown MCP resource")
            .AddErrorCode(McpErrorCodes.UnknownPrompt, 404, "Unknown MCP prompt")
            .AddErrorCode(McpErrorCodes.TypeNotAvailable, 404, "Content type not available")
            .AddErrorCode(McpErrorCodes.SingletonEmpty, 404, "Singleton has no entry")
            .AddErrorCode(McpErrorCodes.UploadSourceRefused, 400, "Upload URL refused")
            .AddErrorCode(McpErrorCodes.UploadSourceFailed, 502, "Upload URL not fetched");
    }

    public void Configure(IPluginAppBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<IOptions<McpOptions>>().Value;
        app.MapProtectedResource(Prefix, new ProtectedResourceOptions { Scope = Scope, Origin = Origin.Mcp, Name = options.Title }).LimitBody(options.MaxRequestBytes).MapMcp();
        app.MapManagement("/mcp/clients").MapMcpClientEndpoints();
        McpSiteSetup.Map(app);
    }
}

public sealed class McpPermissions : IPermissionProvider
{
    // Managing the MCP clients, the agent instructions and the stored prompts of a site.
    public const string Manage = "mcp.manage";

    public IEnumerable<Permission> GetPermissions() => [new Permission(Manage, "Manage the MCP clients, agent instructions and prompts of a site.")];
}
