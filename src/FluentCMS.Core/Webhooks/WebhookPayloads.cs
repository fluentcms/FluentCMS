using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;

namespace FluentCMS.Core.Webhooks;

// What a delivery carries, in the Standard Webhooks shape: the event type, the delivery's id (also its webhook-id), when it happened,
// and its data. A publish names the set,
// its rationale, sources and items, with a link to the set in the admin; a revert's publish also names the set it reverted.
public static class WebhookPayloads
{
    // A new delivery's id, the same for every attempt at it, so a receiver can tell a retry from a new event.
    public static string NewDeliveryId()
    {
        return $"msg_{Guid.CreateVersion7():N}";
    }

    public static string ForPublish(string id, DateTimeOffset at, ChangeSet published, ChangeSet? reverted, string? adminUrl)
    {
        ArgumentNullException.ThrowIfNull(published);

        var data = new JsonObject
        {
            ["changeSet"] = SetOf(published, adminUrl),
            ["items"] = new JsonArray([.. published.Operations.Select(operation => (JsonNode)new JsonObject
            {
                ["kind"] = Name(operation.ItemKind),
                ["id"] = operation.ItemId.ToString(),
                ["operation"] = Name(operation.Kind),
                ["version"] = operation.ProposedVersion,
            })]),
        };
        if (reverted is not null)
        {
            data["reverted"] = SetOf(reverted, adminUrl);
        }

        return Envelope(reverted is null ? WebhookEvents.ChangeSetPublished : WebhookEvents.ChangeSetReverted, id, at, data);
    }

    public static string ForTest(string id, DateTimeOffset at, Guid subscriptionId)
    {
        return Envelope(WebhookEvents.Test, id, at, new JsonObject
        {
            ["subscription"] = subscriptionId.ToString(),
            ["message"] = "A test delivery from FluentCMS; it changes nothing.",
        });
    }

    private static string Envelope(string type, string id, DateTimeOffset at, JsonObject data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return new JsonObject
        {
            ["type"] = type,
            ["id"] = id,
            ["timestamp"] = at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["data"] = data,
        }.ToJsonString();
    }

    // The set's page in the admin: absolute when the admin's address is configured, a path below it otherwise.
    private static JsonObject SetOf(ChangeSet set, string? adminUrl)
    {
        var path = $"sets/{set.Id}";
        return new JsonObject
        {
            ["id"] = set.Id.ToString(),
            ["rationale"] = set.Rationale,
            ["sources"] = new JsonArray([.. set.Sources.Select(source => (JsonNode)source)]),
            ["link"] = string.IsNullOrWhiteSpace(adminUrl) ? "/" + path : adminUrl.TrimEnd('/') + "/" + path,
        };
    }

    private static string Name(Enum value)
    {
        return value.ToString().ToLowerInvariant();
    }
}
