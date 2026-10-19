using FluentCMS.Core.Results;

namespace FluentCMS.Core.Webhooks;

// The endpoints told when a change set is published or reverted (API-05). Adding and disabling each append their audit event in
// the same unit of work (AUD-N1), and only a person may list, add or disable one: a subscription sends content out of the product,
// which no agent or engine decides (SEC-02).
public interface IWebhookDirectory
{
    Task<Result<IReadOnlyList<WebhookSubscription>>> ListAsync(CancellationToken cancellationToken = default);

    // The signing secret is in the result and nowhere else readable; it is stored encrypted and never listed.
    Task<Result<AddedWebhookSubscription>> AddAsync(NewWebhookSubscription subscription, CancellationToken cancellationToken = default);

    Task<Result<WebhookSubscription>> DisableAsync(Guid id, CancellationToken cancellationToken = default);

    // The latest attempts to deliver to the subscription, newest first.
    Task<Result<IReadOnlyList<WebhookDelivery>>> DeliveriesAsync(Guid id, int limit = 10, CancellationToken cancellationToken = default);

    // Queues a test delivery, sent and logged as any other, so an administrator can check the endpoint and its signature check.
    Task<Result> SendTestAsync(Guid id, CancellationToken cancellationToken = default);
}

// One attempt to deliver an event: the status the endpoint answered, or why there was none.
public sealed record WebhookDelivery(
    Guid Id,
    Guid SubscriptionId,
    string DeliveryId,
    string Event,
    int Attempt,
    DateTimeOffset AttemptedAt,
    int? StatusCode,
    string? Error,
    TimeSpan Duration)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

public sealed record NewWebhookSubscription(string Url, IReadOnlyList<string> Events);

public sealed record AddedWebhookSubscription(WebhookSubscription Subscription, string Secret);
