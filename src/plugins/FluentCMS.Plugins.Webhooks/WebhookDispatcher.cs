using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Content;
using FluentCMS.Events;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Webhooks;

// The outbox handler of every event a webhook can subscribe to.
internal sealed class WebhookEventHandler<TEvent>(WebhookDispatcher dispatcher) : IOutboxEventHandler<TEvent>
    where TEvent : CmsEvent
{
    public ValueTask HandleAsync(TEvent message, EventDelivery delivery, CancellationToken cancellationToken) =>
        dispatcher.DispatchAsync(message, delivery, cancellationToken);
}

// Matches an event against the enabled webhooks of its site, by event name and content type, and enqueues the first
// attempt of a delivery to each. The delivery id follows from the outbox delivery and the webhook, so a handler that runs
// again enqueues jobs that find their attempt claimed.
internal sealed class WebhookDispatcher(PluginDbContext<WebhooksPlugin> database, IJobScheduler scheduler)
{
    // The correlation id that core gives the events of an import started with webhooks=true (spec 4.11).
    internal const string ImportWithWebhooks = "import+webhooks:";

    public async ValueTask DispatchAsync(CmsEvent message, EventDelivery delivery, CancellationToken cancellationToken)
    {
        if (!Delivers(message.Source))
        {
            return;
        }

        var name = WebhookEvents.Names[message.GetType()];
        var site = message.Source.SiteId;
        var type = (message as EntryEvent)?.Type;
        var webhooks = await database.Set<Webhook>().AsNoTracking()
            .Where(webhook => webhook.SiteId == site && webhook.Enabled)
            .Select(webhook => new { webhook.Id, webhook.Events, webhook.Types })
            .ToListAsync(cancellationToken);

        var body = WebhookPayload.Of(message, name);
        foreach (var webhook in webhooks)
        {
            if (!WebhookJson.List(webhook.Events).Contains(name))
            {
                continue;
            }

            var types = WebhookJson.List(webhook.Types);
            if (type is not null && types.Count > 0 && !types.Contains(type))
            {
                continue;
            }

            var attempt = new WebhookAttempt(webhook.Id, DeliveryIdOf(delivery.Key, webhook.Id), message.EventId, name, 1, Manual: false, body);
            await scheduler.EnqueueAsync<WebhookDeliveryJob, WebhookAttempt>(attempt, WebhookDeliveryJob.Options(site), cancellationToken);
        }
    }

    // What an import writes is delivered only when the import asked for it.
    internal static bool Delivers(EventSource source) =>
        source.Origin != Origin.Import || source.CorrelationId.StartsWith(ImportWithWebhooks, StringComparison.Ordinal);

    private static Guid DeliveryIdOf(Guid key, Guid webhook)
    {
        Span<byte> input = stackalloc byte[32];
        key.TryWriteBytes(input);
        webhook.TryWriteBytes(input[16..]);
        return new Guid(SHA256.HashData(input)[..16]);
    }
}

// The body of a delivery: { version, id, event, occurredAt, site, origin, actor, data, links }. Links are relative to the
// root of the instance, behind its path base.
internal static class WebhookPayload
{
    // Changes only with a change of the body's shape that a receiver of the current version could misread.
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Of(CmsEvent message, string name)
    {
        var source = message.Source;
        object data = message switch
        {
            EntryEvent entry => new EntryData(entry.Type, entry.EntryId, entry.Locale, WebhookEvents.StatusOf(entry), entry.Version),
            CacheInvalidatedEvent cache => new CacheData(cache.Tags),
            MediaAssetEvent asset => new MediaData(asset.AssetId, asset.FolderId, asset.FileName),
            _ => new { },
        };
        object links = message switch
        {
            EntryEvent linked => new EntryLinks($"api/management/v1/content/{linked.Type}/{linked.EntryId:D}", $"api/delivery/v1/{linked.Type}/{linked.EntryId:D}"),
            MediaAssetEvent asset => new MediaLinks($"api/management/v1/media/{asset.AssetId:D}"),
            _ => new { },
        };

        return Serialize(message.EventId, name, source.OccurredAt, source.SiteId, source.Origin, new Actor(source.ActorKind, source.ActorId, source.ActorName), data, links);
    }

    public static string Ping(Guid id, DateTimeOffset at, OperationContext operation)
    {
        var actor = operation.AuditPrincipal;
        return Serialize(id, WebhookEvents.Ping, at, operation.Site.Value, operation.Origin, new Actor(actor.Kind, actor.Id, actor.Name), new { }, new { });
    }

    private static string Serialize(Guid id, string name, DateTimeOffset occurredAt, Guid site, Origin origin, Actor actor, object data, object links) =>
        JsonSerializer.Serialize(new Body(Version, id, name, occurredAt, site, origin, actor, data, links), Options);

    private sealed record Body(int Version, Guid Id, string Event, DateTimeOffset OccurredAt, Guid Site, Origin Origin, Actor Actor, object Data, object Links);

    private sealed record Actor(PrincipalKind Kind, Guid Id, string Name);

    private sealed record EntryData(string Type, Guid DocumentId, string Locale, string Status, int Version);

    private sealed record CacheData(IReadOnlyList<string> Tags);

    private sealed record EntryLinks(string Management, string Delivery);

    private sealed record MediaData(Guid AssetId, Guid? FolderId, string FileName);

    private sealed record MediaLinks(string Management);
}

internal static class WebhookJson
{
    public static IReadOnlyList<string> List(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    public static IReadOnlyDictionary<string, string> Map(string json) => JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    public static string Of<T>(T value) => JsonSerializer.Serialize(value);
}
