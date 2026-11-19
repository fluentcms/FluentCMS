using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Webhooks;

internal sealed record WebhookQueuePayload;

// Wakes each webhook queue that has stalled: its head and its last claim are older than StalledAfter, and no attempt is in
// flight, or only an abandoned one. The job that should have woken its head was lost, or the claim's job is gone, or the
// webhook was disabled while nothing was in flight. The wait leaves alone a queue whose head's job is on its way through a
// busy webhooks queue. A site where the plugin is disabled is skipped, since its jobs send nothing.
internal sealed class WebhookQueueJob(PluginDbContext<WebhooksPlugin> database, IJobScheduler scheduler, IPluginState plugins, IOptions<WebhooksOptions> options, TimeProvider time, ILogger<WebhookQueueJob> logger)
    : IJob<WebhookQueuePayload>
{
    public const string Type = "webhooks.queue";
    public const string Schedule = "* * * * *";

    public static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(10);

    // Whether the head and the webhook's last claim are both older than StalledAfter. The head's created_at is the time of its
    // event, which is old for a late retry or a backlog of the outbox, so the last claim tells a queue that drains from one
    // that waits. A claim's created_at is when it was claimed.
    internal static async Task<bool> HasStalledAsync(IQueryable<WebhookDelivery> deliveries, Guid webhook, WebhookDelivery head, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stalled = now - StalledAfter;
        return head.CreatedAt <= stalled
            && !await deliveries.AnyAsync(item => item.WebhookId == webhook && item.State != DeliveryStates.Queued && item.CreatedAt > stalled, cancellationToken);
    }

    public async Task ExecuteAsync(WebhookQueuePayload payload, JobContext context)
    {
        var cancellationToken = context.CancellationToken;
        var deliveries = database.Set<WebhookDelivery>().AsNoTracking();
        var now = time.GetUtcNow();
        var abandoned = now - options.Value.Timeout - WebhookDeliveryJob.AbandonMargin;
        var waiting = await deliveries.Where(item => item.State == DeliveryStates.Queued).Select(item => item.WebhookId).Distinct().ToListAsync(cancellationToken);
        var woken = 0;
        foreach (var webhook in waiting)
        {
            var site = await database.Set<Webhook>().AsNoTracking().Where(item => item.Id == webhook).Select(item => (Guid?)item.SiteId).FirstOrDefaultAsync(cancellationToken);
            if (site is null || !await plugins.IsEnabledAsync(WebhooksPlugin.Name, new SiteId(site.Value), cancellationToken))
            {
                continue;
            }

            if (await WebhookDeliveryJob.Head(deliveries, webhook).FirstOrDefaultAsync(cancellationToken) is not { } head
                || await deliveries.AnyAsync(item => item.WebhookId == webhook && item.State == DeliveryStates.Pending && item.CreatedAt > abandoned, cancellationToken)
                || !await HasStalledAsync(deliveries, webhook, head, now, cancellationToken))
            {
                continue;
            }

            await scheduler.EnqueueAsync<WebhookDeliveryJob, WebhookAttempt>(WebhookDeliveryJob.AttemptOf(head), WebhookDeliveryJob.Options(site.Value), cancellationToken);
            woken++;
        }

        if (woken > 0)
        {
            WebhookLog.QueuesWoken(logger, woken);
        }
    }
}
