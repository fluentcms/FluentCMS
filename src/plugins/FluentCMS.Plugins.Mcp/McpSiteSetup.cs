using FluentCMS.Activity;
using FluentCMS.Content;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace FluentCMS.Plugins.Mcp;

// What a site adds to the server for its agents: the schema, guidelines and instruction resources, the stored prompts,
// their tables and the Management endpoints that keep them.
internal static class McpSiteSetup
{
    public static void Add(IPluginServiceBuilder plugin)
    {
        plugin.Services.AddScoped<McpInstructions>();
        plugin.Services.AddScoped<McpInstructionService>();
        plugin.Services.AddScoped<McpStoredPrompts>();
        plugin.Services.AddScoped<McpPromptService>();

        plugin
            .AddModel<McpModel>()
            .AddMigrations(typeof(McpPlugin).Assembly)
            .AddMcpResources<McpSiteResources>()
            .AddMcpPrompts<McpStoredPromptProvider>()
            .AddErrorCode(McpErrorCodes.InvalidInstruction, 422, "Invalid agent instructions")
            .AddErrorCode(McpErrorCodes.InstructionTypeNotFound, 404, "Content type not found")
            .AddErrorCode(McpErrorCodes.InstructionNotFound, 404, "Agent instructions not found")
            .AddErrorCode(McpErrorCodes.InvalidPrompt, 422, "Invalid stored prompt")
            .AddErrorCode(McpErrorCodes.PromptNotFound, 404, "Stored prompt not found")
            .AddErrorCode(McpErrorCodes.PromptNameTaken, 409, "Stored prompt name taken")
            .AddErrorCode(McpErrorCodes.PromptNameReserved, 409, "Prompt name taken by a plugin")
            .AddErrorCode(McpErrorCodes.UndeclaredPromptArgument, 422, "Undeclared prompt argument")
            .AddErrorCode(McpErrorCodes.MissingPromptArgument, 422, "Missing prompt argument")
            .AddActivity<McpInstructionSavedEvent>("mcp.instruction_saved", saved => Instruction("mcp.instruction_saved", saved.Type))
            .AddActivity<McpInstructionDeletedEvent>("mcp.instruction_deleted", deleted => Instruction("mcp.instruction_deleted", deleted.Type))
            .AddActivity<McpPromptCreatedEvent>("mcp.prompt_created", created => Prompt("mcp.prompt_created", created.PromptId, created.Name))
            .AddActivity<McpPromptUpdatedEvent>("mcp.prompt_updated", updated => Prompt("mcp.prompt_updated", updated.PromptId, updated.Name))
            .AddActivity<McpPromptDeletedEvent>("mcp.prompt_deleted", deleted => Prompt("mcp.prompt_deleted", deleted.PromptId, deleted.Name));
    }

    public static void Map(IPluginAppBuilder app)
    {
        app.MapManagement(McpManagementEndpoints.InstructionsPrefix).MapInstructionEndpoints();
        app.MapManagement(McpManagementEndpoints.PromptsPrefix).MapPromptEndpoints();
    }

    // The guidelines of the site are the instructions of the handshake. The server is stateless, so the options of a
    // request are its own, and one site's guidelines never reach a request of another.
    public static async Task InstructAsync(HttpContext http, McpServerOptions server, CancellationToken cancellationToken)
    {
        if (http.RequestServices.GetRequiredService<IOperationContextAccessor>().Current is { } operation)
        {
            server.ServerInstructions = await http.RequestServices.GetRequiredService<McpInstructions>().GuidelinesAsync(operation.Site, cancellationToken);
        }
    }

    // The guidelines are the site's, which the row names; the instructions of a type are named by the type.
    private static ActivityEntry Instruction(string action, string? type) => new(action)
    {
        TargetType = type is null ? "mcp_guidelines" : "mcp_instruction",
        TargetId = type,
        TargetLabel = type,
    };

    private static ActivityEntry Prompt(string action, Guid id, string name) => new(action)
    {
        TargetType = "mcp_prompt",
        TargetId = id.ToString("D"),
        TargetLabel = name,
    };
}
