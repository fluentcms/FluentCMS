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
}

public sealed record NewWebhookSubscription(string Url, IReadOnlyList<string> Events);

public sealed record AddedWebhookSubscription(WebhookSubscription Subscription, string Secret);
