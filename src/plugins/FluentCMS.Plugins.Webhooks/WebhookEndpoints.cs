using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins.Webhooks;

// /api/management/v1/webhooks. The service checks webhooks.manage for the site of the request.
internal static class WebhookEndpoints
{
    public static RouteGroupBuilder MapWebhookEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Ok(new { items = await webhooks.ListAsync(cancellationToken) }));

        group.MapPost("", async (WebhookRequest request, WebhookService webhooks, CancellationToken cancellationToken) =>
        {
            var created = await webhooks.CreateAsync(request, cancellationToken);
            return Results.Created($"webhooks/{created.Id:D}", created);
        });

        group.MapGet("/{id:guid}", async (Guid id, WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Ok(await webhooks.GetAsync(id, cancellationToken)));

        group.MapPut("/{id:guid}", async (Guid id, WebhookRequest request, WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Ok(await webhooks.UpdateAsync(id, request, cancellationToken)));

        group.MapDelete("/{id:guid}", async (Guid id, WebhookService webhooks, CancellationToken cancellationToken) =>
        {
            await webhooks.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/deliveries", async (Guid id, int? page, int? pageSize, WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Ok(await webhooks.DeliveriesAsync(id, page ?? 1, pageSize ?? 20, cancellationToken)));

        group.MapPost("/{id:guid}/deliveries/{delivery:guid}/redeliver", async (Guid id, Guid delivery, WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Accepted(value: new { deliveryId = await webhooks.RedeliverAsync(id, delivery, cancellationToken) }));

        group.MapPost("/{id:guid}/ping", async (Guid id, WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Accepted(value: new { deliveryId = await webhooks.PingAsync(id, cancellationToken) }));

        return group;
    }
}
