using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins.Mcp;

// /api/management/v1/mcp/clients. The service checks mcp.manage for the instance.
internal static class McpClientEndpoints
{
    public static RouteGroupBuilder MapMcpClientEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (McpClientService clients, CancellationToken cancellationToken) =>
            Results.Ok(new { items = await clients.ListAsync(cancellationToken) }));

        group.MapPost("/{id:guid}/approve", async (Guid id, McpClientApproval? approval, McpClientService clients, CancellationToken cancellationToken) =>
            Results.Ok(await clients.ApproveAsync(id, approval, cancellationToken)));

        group.MapPost("/{id:guid}/reject", async (Guid id, McpClientService clients, CancellationToken cancellationToken) =>
            Results.Ok(await clients.RejectAsync(id, cancellationToken)));

        group.MapDelete("/{id:guid}", async (Guid id, McpClientService clients, CancellationToken cancellationToken) =>
        {
            await clients.RevokeAsync(id, cancellationToken);
            return Results.NoContent();
        });

        return group;
    }
}
