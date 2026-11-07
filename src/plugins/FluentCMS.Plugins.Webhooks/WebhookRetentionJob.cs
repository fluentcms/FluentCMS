using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Webhooks;

internal sealed record WebhookRetentionPayload;

// Removes the attempts older than WebhooksOptions.Retention, in batches.
internal sealed class WebhookRetentionJob(PluginDbContext<WebhooksPlugin> database, IOptions<WebhooksOptions> options, TimeProvider time, ILogger<WebhookRetentionJob> logger)
    : IJob<WebhookRetentionPayload>
{
    public const string Type = "webhooks.retention";
    public const string Schedule = "15 4 * * *";
    public const int BatchSize = 500;

    public async Task ExecuteAsync(WebhookRetentionPayload payload, JobContext context)
    {
        var cutoff = time.GetUtcNow() - options.Value.Retention;
        var removed = 0;
        while (true)
        {
            var ids = await database.Set<WebhookDelivery>()
                .Where(delivery => delivery.CreatedAt < cutoff)
                .OrderBy(delivery => delivery.CreatedAt)
                .Select(delivery => delivery.Id)
                .Take(BatchSize)
                .ToListAsync(context.CancellationToken);
            if (ids.Count == 0)
            {
                break;
            }

            removed += await database.Set<WebhookDelivery>().Where(delivery => ids.Contains(delivery.Id)).ExecuteDeleteAsync(context.CancellationToken);
        }

        WebhookLog.Pruned(logger, removed);
    }
}
