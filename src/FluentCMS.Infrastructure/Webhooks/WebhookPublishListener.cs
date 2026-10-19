using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Webhooks;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Webhooks;

// Queues one delivery per enabled subscription that wants the event, in the publish's unit of work, so a delivery exists exactly when
// the publish committed. A revert's publish is the reverted event alone, naming both sets.
internal sealed class WebhookPublishListener(
    FluentCmsDbContext context,
    IJobQueue jobs,
    TimeProvider clock,
    IOptions<WebhookOptions> options) : IChangeSetEvents
{
    public async Task PublishedAsync(ChangeSet published, ChangeSet? reverted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(published);

        var eventName = reverted is null ? WebhookEvents.ChangeSetPublished : WebhookEvents.ChangeSetReverted;
        var subscriptions = await context.WebhookSubscriptions
            .AsNoTracking()
            .Where(row => row.DisabledAt == null && row.Events.Contains(eventName))
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var settings = options.Value;
        var body = WebhookPayloads.ForPublish(clock.GetUtcNow(), published, reverted, settings.AdminUrl);
        foreach (var subscription in subscriptions)
        {
            await jobs.EnqueueAsync(WebhookDeliveries.Job(subscription, eventName, published.Id, body, settings.MaxAttempts), cancellationToken);
        }
    }
}
