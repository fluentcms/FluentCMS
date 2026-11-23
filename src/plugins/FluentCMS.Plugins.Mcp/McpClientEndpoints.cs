using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins.Mcp;

internal sealed record McpClientList(IReadOnlyList<McpClientInfo> Items);

internal sealed record McpConsentList(IReadOnlyList<McpConsentInfo> Items);

// /api/management/v1/mcp/clients, where the service checks mcp.manage for the instance, and /mcp/consents, the caller's own.
internal static class McpClientEndpoints
{
    public static RouteGroupBuilder MapMcpConsentEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (McpClientService clients, CancellationToken cancellationToken) =>
            Results.Ok(new McpConsentList(await clients.ListConsentsAsync(cancellationToken)))).Produces<McpConsentList>();

        group.MapDelete("/{id:guid}", async (Guid id, McpClientService clients, CancellationToken cancellationToken) =>
        {
            await clients.EndConsentAsync(id, cancellationToken);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent);

        return group;
    }

    public static RouteGroupBuilder MapMcpClientEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (McpClientService clients, CancellationToken cancellationToken) =>
            Results.Ok(new McpClientList(await clients.ListAsync(cancellationToken)))).Produces<McpClientList>();

        group.MapPost("", async (McpClientRegistrationRequest? request, McpClientService clients, CancellationToken cancellationToken) =>
        {
            var client = await clients.RegisterAsync(request, cancellationToken);
            return Results.Created($"mcp/clients/{client.Id:D}", client);
        }).Produces<McpClientInfo>(StatusCodes.Status201Created);

        group.MapPost("/remove", async (McpClientRemoval? removal, McpClientService clients, CancellationToken cancellationToken) =>
            Results.Ok(await clients.RemoveOpenApprovedAsync(removal, cancellationToken))).Produces<McpClientRemovalResult>();

        group.MapPost("/{id:guid}/approve", async (Guid id, McpClientApproval? approval, McpClientService clients, CancellationToken cancellationToken) =>
            Results.Ok(await clients.ApproveAsync(id, approval, cancellationToken))).Produces<McpClientInfo>();

        group.MapPut("/{id:guid}/role", async (Guid id, McpClientRoleChange? change, McpClientService clients, CancellationToken cancellationToken) =>
            Results.Ok(await clients.ChangeRoleAsync(id, change, cancellationToken))).Produces<McpClientInfo>();

        group.MapPost("/{id:guid}/reject", async (Guid id, McpClientService clients, CancellationToken cancellationToken) =>
            Results.Ok(await clients.RejectAsync(id, cancellationToken))).Produces<McpClientInfo>();

        group.MapDelete("/{id:guid}", async (Guid id, McpClientService clients, CancellationToken cancellationToken) =>
        {
            await clients.RevokeAsync(id, cancellationToken);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent);

        return group;
    }
}
