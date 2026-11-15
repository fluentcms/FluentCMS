using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins.Webhooks;

internal sealed record WebhookList(IReadOnlyList<WebhookInfo> Items);

// A delivery queued by a redeliver or a ping, which the deliveries of the webhook list once it is attempted.
internal sealed record WebhookDeliveryQueued(Guid DeliveryId);

// /api/management/v1/webhooks. The service checks webhooks.manage for the site of the request.
internal static class WebhookEndpoints
{
    public static RouteGroupBuilder MapWebhookEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Ok(new WebhookList(await webhooks.ListAsync(cancellationToken)))).Produces<WebhookList>();

        group.MapPost("", async (WebhookRequest request, WebhookService webhooks, CancellationToken cancellationToken) =>
        {
            var created = await webhooks.CreateAsync(request, cancellationToken);
            return Results.Created($"webhooks/{created.Id:D}", created);
        }).Produces<WebhookInfo>(StatusCodes.Status201Created);

        group.MapGet("/{id:guid}", async (Guid id, WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Ok(await webhooks.GetAsync(id, cancellationToken))).Produces<WebhookInfo>();

        group.MapPut("/{id:guid}", async (Guid id, WebhookRequest request, WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Ok(await webhooks.UpdateAsync(id, request, cancellationToken))).Produces<WebhookInfo>();

        group.MapDelete("/{id:guid}", async (Guid id, WebhookService webhooks, CancellationToken cancellationToken) =>
        {
            await webhooks.DeleteAsync(id, cancellationToken);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent);

        group.MapGet("/{id:guid}/deliveries", async (Guid id, int? page, int? pageSize, WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Ok(await webhooks.DeliveriesAsync(id, page ?? 1, pageSize ?? 20, cancellationToken))).Produces<DeliveryPage>();

        group.MapPost("/{id:guid}/deliveries/{delivery:guid}/redeliver", async (Guid id, Guid delivery, WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Accepted(value: new WebhookDeliveryQueued(await webhooks.RedeliverAsync(id, delivery, cancellationToken))))
            .Produces<WebhookDeliveryQueued>(StatusCodes.Status202Accepted);

        group.MapPost("/{id:guid}/ping", async (Guid id, WebhookService webhooks, CancellationToken cancellationToken) =>
            Results.Accepted(value: new WebhookDeliveryQueued(await webhooks.PingAsync(id, cancellationToken))))
            .Produces<WebhookDeliveryQueued>(StatusCodes.Status202Accepted);

        return group;
    }
}
