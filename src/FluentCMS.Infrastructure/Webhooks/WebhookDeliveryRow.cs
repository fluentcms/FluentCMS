namespace FluentCMS.Infrastructure.Webhooks;

// One attempt to deliver an event to a subscription. It never holds the secret or the signature.
public sealed class WebhookDeliveryRow
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public string DeliveryId { get; set; } = string.Empty;

    public string Event { get; set; } = string.Empty;

    public Guid? ChangeSetId { get; set; }

    public int Attempt { get; set; }

    public DateTimeOffset AttemptedAt { get; set; }

    public int? StatusCode { get; set; }

    public string? Error { get; set; }

    public TimeSpan Duration { get; set; }
}
