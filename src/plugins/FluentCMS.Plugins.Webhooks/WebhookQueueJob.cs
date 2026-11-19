using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Webhooks;

internal sealed record WebhookQueuePayload;

// Wakes each webhook queue that holds attempts but has no attempt in flight, or only an abandoned one: the job that should
// have woken its head was lost, or the claim's job is gone, or the webhook was disabled while nothing was in flight. A head
// whose job is already on its way gets a second job, which finds the attempt claimed and sends nothing.
internal sealed class WebhookQueueJob(PluginDbContext<WebhooksPlugin> database, IJobScheduler scheduler, IOptions<WebhooksOptions> options, TimeProvider time, ILogger<WebhookQueueJob> logger)
    : IJob<WebhookQueuePayload>
{
    public const string Type = "webhooks.queue";
    public const string Schedule = "* * * * *";

    public async Task ExecuteAsync(WebhookQueuePayload payload, JobContext context)
    {
        var cancellationToken = context.CancellationToken;
        var deliveries = database.Set<WebhookDelivery>().AsNoTracking();
        var abandoned = time.GetUtcNow() - options.Value.Timeout - WebhookDeliveryJob.AbandonMargin;
        var waiting = await deliveries.Where(item => item.State == DeliveryStates.Queued).Select(item => item.WebhookId).Distinct().ToListAsync(cancellationToken);
        var woken = 0;
        foreach (var webhook in waiting)
        {
            if (await deliveries.AnyAsync(item => item.WebhookId == webhook && item.State == DeliveryStates.Pending && item.CreatedAt > abandoned, cancellationToken))
            {
                continue;
            }

            var head = await deliveries
                .Where(item => item.WebhookId == webhook && item.State == DeliveryStates.Queued)
                .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            var site = await database.Set<Webhook>().AsNoTracking().Where(item => item.Id == webhook).Select(item => (Guid?)item.SiteId).FirstOrDefaultAsync(cancellationToken);
            if (head is null || site is null)
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
