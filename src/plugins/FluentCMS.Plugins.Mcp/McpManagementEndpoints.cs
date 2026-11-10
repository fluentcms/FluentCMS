using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins.Mcp;

// /api/management/v1/mcp/instructions and /api/management/v1/mcp/prompts. The services check mcp.manage for the site of
// the request.
internal static class McpManagementEndpoints
{
    public const string InstructionsPrefix = "/mcp/instructions";
    public const string PromptsPrefix = "/mcp/prompts";

    // The group itself is the guidelines of the site, and a type under it the instructions of that type.
    public static RouteGroupBuilder MapInstructionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (McpInstructionService instructions, CancellationToken cancellationToken) =>
            Results.Ok(new { items = await instructions.ListAsync(cancellationToken) }));

        group.MapPut("", async (McpInstructionRequest request, McpInstructionService instructions, CancellationToken cancellationToken) =>
            Results.Ok(await instructions.SaveAsync(null, request, cancellationToken)));

        group.MapDelete("", async (McpInstructionService instructions, CancellationToken cancellationToken) =>
        {
            await instructions.DeleteAsync(null, cancellationToken);
            return Results.NoContent();
        });

        group.MapPut("/{type}", async (string type, McpInstructionRequest request, McpInstructionService instructions, CancellationToken cancellationToken) =>
            Results.Ok(await instructions.SaveAsync(type, request, cancellationToken)));

        group.MapDelete("/{type}", async (string type, McpInstructionService instructions, CancellationToken cancellationToken) =>
        {
            await instructions.DeleteAsync(type, cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    public static RouteGroupBuilder MapPromptEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (McpPromptService prompts, CancellationToken cancellationToken) =>
            Results.Ok(new { items = await prompts.ListAsync(cancellationToken) }));

        group.MapPost("", async (McpPromptRequest request, McpPromptService prompts, CancellationToken cancellationToken) =>
        {
            var created = await prompts.CreateAsync(request, cancellationToken);
            return Results.Created($"mcp/prompts/{created.Id:D}", created);
        });

        group.MapGet("/{id:guid}", async (Guid id, McpPromptService prompts, CancellationToken cancellationToken) =>
            Results.Ok(await prompts.GetAsync(id, cancellationToken)));

        group.MapPut("/{id:guid}", async (Guid id, McpPromptRequest request, McpPromptService prompts, CancellationToken cancellationToken) =>
            Results.Ok(await prompts.UpdateAsync(id, request, cancellationToken)));

        group.MapDelete("/{id:guid}", async (Guid id, McpPromptService prompts, CancellationToken cancellationToken) =>
        {
            await prompts.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        });

        return group;
    }
}
