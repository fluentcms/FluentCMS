using System.Text.Json;
using FluentCMS.Core.Jobs;

namespace FluentCMS.Infrastructure.Webhooks;

// A delivery is a job that carries the body it sends, built when the event happened, so every attempt sends the same request
// under the same webhook-id and a receiver can tell a retry from a new event.
internal static class WebhookDeliveries
{
    public const string JobType = "webhook.deliver";

    public const string SubjectType = "webhook_subscription";

    public static NewJob Job(Guid subscriptionId, string eventName, Guid? changeSetId, string body, int maxAttempts)
    {
        var payload = JsonSerializer.SerializeToElement(new DeliveryPayload(subscriptionId, $"msg_{Guid.CreateVersion7():N}", eventName, changeSetId, body));
        return new NewJob(JobType, payload) { Subject = new JobSubject(SubjectType, subscriptionId), MaxAttempts = maxAttempts };
    }

    public static DeliveryPayload? Read(JsonElement payload)
    {
        try
        {
            return payload.ValueKind == JsonValueKind.Object
                && payload.Deserialize<DeliveryPayload>() is { SubscriptionId: var id, DeliveryId.Length: > 0, Event.Length: > 0, Body.Length: > 0 } delivery
                && id != Guid.Empty
                    ? delivery
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed record DeliveryPayload(Guid SubscriptionId, string DeliveryId, string Event, Guid? ChangeSetId, string Body);
}
