using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Webhooks;

// One attempt of a delivery. Body is the exact JSON every attempt sends. A manual one, a ping or a redelivery, is tried
// once and is sent even when the webhook is disabled.
internal sealed record WebhookAttempt(Guid WebhookId, Guid DeliveryId, Guid EventId, string Event, int Attempt, bool Manual, string Body);

// Records each attempt and schedules the next one itself, on the plugin's schedule rather than the runner's backoff, so the
// job fails only when the database does.
internal sealed class WebhookDeliveryJob(
    PluginDbContext<WebhooksPlugin> database,
    WebhookSender sender,
    WebhookSigning signing,
    IJobScheduler scheduler,
    TimeProvider time,
    ILogger<WebhookDeliveryJob> logger) : IJob<WebhookAttempt>
{
    public const string Type = "webhooks.deliver";

    public static readonly IReadOnlyList<TimeSpan> Retries =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24)];

    public async Task ExecuteAsync(WebhookAttempt attempt, JobContext context)
    {
        var cancellationToken = context.CancellationToken;
        var deliveries = database.Set<WebhookDelivery>();

        // A repeat of a job whose attempt is recorded, or a second job for the same delivery.
        if (await deliveries.AnyAsync(item => item.DeliveryId == attempt.DeliveryId && item.Attempt == attempt.Attempt, cancellationToken))
        {
            return;
        }

        var webhook = await database.Set<Webhook>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == attempt.WebhookId, cancellationToken);
        if (webhook is null || (!webhook.Enabled && !attempt.Manual))
        {
            // The attempt before this one said another would follow; now none does.
            await deliveries
                .Where(item => item.DeliveryId == attempt.DeliveryId && item.Attempt == attempt.Attempt - 1 && item.State == DeliveryStates.Failed)
                .ExecuteUpdateAsync(item => item.SetProperty(row => row.State, DeliveryStates.Dead).SetProperty(row => row.NextRetryAt, (DateTimeOffset?)null), cancellationToken);
            WebhookLog.Skipped(logger, attempt.WebhookId, attempt.DeliveryId, attempt.Attempt);
            return;
        }

        var result = await SendAsync(webhook, attempt, cancellationToken);
        DateTimeOffset? retryAt = !result.Succeeded && !attempt.Manual && attempt.Attempt <= Retries.Count ? time.GetUtcNow() + Retries[attempt.Attempt - 1] : null;
        if (retryAt is { } at)
        {
            await scheduler.ScheduleAsync<WebhookDeliveryJob, WebhookAttempt>(
                attempt with { Attempt = attempt.Attempt + 1 },
                at,
                new JobOptions { Site = new SiteId(webhook.SiteId) },
                cancellationToken);
        }

        var state = result.Succeeded ? DeliveryStates.Succeeded : retryAt is null ? DeliveryStates.Dead : DeliveryStates.Failed;
        deliveries.Add(new WebhookDelivery
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = attempt.DeliveryId,
            WebhookId = webhook.Id,
            EventId = attempt.EventId,
            Event = attempt.Event,
            Attempt = attempt.Attempt,
            State = state,
            StatusCode = result.StatusCode,
            DurationMs = result.DurationMs,
            RequestBody = attempt.Body,
            ResponseExcerpt = result.Response,
            Error = result.Error,
            NextRetryAt = retryAt,
            CreatedAt = time.GetUtcNow(),
        });
        await database.SaveChangesAsync(cancellationToken);

        switch (state)
        {
            case DeliveryStates.Succeeded:
                WebhookLog.Delivered(logger, webhook.Id, attempt.DeliveryId, attempt.Attempt, attempt.Event, result.StatusCode, result.DurationMs);
                break;
            case DeliveryStates.Failed:
                WebhookLog.Failed(logger, webhook.Id, attempt.DeliveryId, attempt.Attempt, attempt.Event, result.Error, retryAt!.Value);
                break;
            default:
                WebhookLog.Dead(logger, webhook.Id, attempt.DeliveryId, attempt.Attempt, attempt.Event, result.Error);
                break;
        }
    }

    private async Task<WebhookResult> SendAsync(Webhook webhook, WebhookAttempt attempt, CancellationToken cancellationToken)
    {
        string secret;
        try
        {
            secret = signing.Unprotect(webhook.Secret);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return new WebhookResult(false, null, 0, null, "The secret of the webhook could not be read; set a new one.");
        }

        return await sender.SendAsync(webhook.Url, secret, WebhookJson.Map(webhook.Headers), attempt, cancellationToken);
    }
}
