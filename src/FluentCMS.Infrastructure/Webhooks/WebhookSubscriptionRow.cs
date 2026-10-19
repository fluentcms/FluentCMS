namespace FluentCMS.Infrastructure.Webhooks;

// A WebhookSubscription with its signing secret, which is stored encrypted and never in plain text (SEC-07).
public sealed class WebhookSubscriptionRow
{
    public Guid Id { get; set; }

    public string Url { get; set; } = string.Empty;

    public List<string> Events { get; set; } = [];

    public string ProtectedSecret { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; }

    public Guid AddedById { get; set; }

    public string AddedByName { get; set; } = string.Empty;

    public DateTimeOffset? DisabledAt { get; set; }
}
