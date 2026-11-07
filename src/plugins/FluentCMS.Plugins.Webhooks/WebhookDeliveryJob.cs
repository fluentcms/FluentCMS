using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Webhooks;

// One attempt of a delivery. Body is the exact JSON every attempt sends. A manual one, a ping or a redelivery, is tried
// once and is sent even when the webhook is disabled.
internal sealed record WebhookAttempt(Guid WebhookId, Guid DeliveryId, Guid EventId, string Event, int Attempt, bool Manual, string Body);

// Claims an attempt with a pending row before sending it, so that a repeat of the job or a second job for the attempt sends
// nothing, and so that one attempt of a webhook is sent at a time. Then it records the result and schedules the next attempt
// itself, on the plugin's schedule rather than the runner's backoff, so the job fails only when the database does.
internal sealed class WebhookDeliveryJob(
    PluginDbContext<WebhooksPlugin> database,
    WebhookSender sender,
    WebhookSigning signing,
    IJobScheduler scheduler,
    IOptions<WebhooksOptions> options,
    TimeProvider time,
    ILogger<WebhookDeliveryJob> logger) : IJob<WebhookAttempt>
{
    public const string Type = "webhooks.deliver";

    // Every attempt runs on its own queue, so that a receiver that hangs holds up other deliveries and no other job.
    public const string Queue = "webhooks";

    public const string Interrupted = "The worker stopped before it recorded the result of this attempt.";

    public static readonly IReadOnlyList<TimeSpan> Retries =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24)];

    // While another attempt to its webhook is in flight, a job waits until that attempt's send must have ended, and at least
    // this long, plus up to as much again so that the waiting jobs do not all return together.
    public static readonly TimeSpan Busy = TimeSpan.FromSeconds(2);

    // A pending attempt is abandoned once the send timeout and this have passed since its claim. It covers the writes around
    // the send and the clocks of the other instances.
    public static readonly TimeSpan AbandonMargin = TimeSpan.FromMinutes(1);

    public static JobOptions Options(Guid site) => new() { Queue = Queue, Site = new SiteId(site) };

    public async Task ExecuteAsync(WebhookAttempt attempt, JobContext context)
    {
        var cancellationToken = context.CancellationToken;
        if (await ClaimAsync(attempt, cancellationToken) is not ({ } webhook, var claim))
        {
            return;
        }

        var result = await SendAsync(webhook, attempt, cancellationToken);
        var retryAt = result.Succeeded ? null : RetryAt(attempt.Attempt, attempt.Manual);

        // Before the result: a second job for the next attempt finds it claimed and sends nothing, while a result recorded
        // without its next attempt would end the delivery.
        if (retryAt is { } at)
        {
            await scheduler.ScheduleAsync<WebhookDeliveryJob, WebhookAttempt>(attempt with { Attempt = attempt.Attempt + 1 }, at, Options(webhook.SiteId), cancellationToken);
        }

        var state = result.Succeeded ? DeliveryStates.Succeeded : retryAt is null ? DeliveryStates.Dead : DeliveryStates.Failed;
        await database.Set<WebhookDelivery>()
            .Where(item => item.Id == claim && item.State == DeliveryStates.Pending)
            .ExecuteUpdateAsync(
                item => item
                    .SetProperty(row => row.State, state)
                    .SetProperty(row => row.StatusCode, result.StatusCode)
                    .SetProperty(row => row.DurationMs, result.DurationMs)
                    .SetProperty(row => row.ResponseExcerpt, result.Response)
                    .SetProperty(row => row.Error, result.Error)
                    .SetProperty(row => row.NextRetryAt, retryAt),
                cancellationToken);

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

    // The webhook and the id of the claimed row, or null when this job sends nothing: the attempt is recorded or in flight,
    // the webhook is gone or disabled, or another attempt to the webhook is in flight and this job has deferred itself.
    private async Task<(Webhook Webhook, Guid Claim)?> ClaimAsync(WebhookAttempt attempt, CancellationToken cancellationToken)
    {
        var deliveries = database.Set<WebhookDelivery>();
        for (var round = 1; ; round++)
        {
            var webhook = await database.Set<Webhook>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == attempt.WebhookId, cancellationToken);
            if (webhook is null)
            {
                WebhookLog.Skipped(logger, attempt.WebhookId, attempt.DeliveryId, attempt.Attempt);
                return null;
            }

            var rows = await deliveries.AsNoTracking()
                .Where(item => (item.DeliveryId == attempt.DeliveryId && item.Attempt == attempt.Attempt) || (item.WebhookId == webhook.Id && item.State == DeliveryStates.Pending))
                .ToListAsync(cancellationToken);
            var now = time.GetUtcNow();

            // A repeat of a job whose attempt is claimed, or a second job for the attempt. A claim that is still pending may be
            // this job's own, left by a crash or a failed write, which nothing else finishes.
            if (rows.Find(item => item.DeliveryId == attempt.DeliveryId && item.Attempt == attempt.Attempt) is { } own)
            {
                if (own.State == DeliveryStates.Pending)
                {
                    await (IsAbandoned(own, now) ? TakeOverAsync(own, webhook, cancellationToken) : DeferAsync(attempt, webhook, AbandonedAt(own), cancellationToken));
                }

                return null;
            }

            if (!webhook.Enabled && !attempt.Manual)
            {
                // The attempt before this one said another would follow; now none does.
                await deliveries
                    .Where(item => item.DeliveryId == attempt.DeliveryId && item.Attempt == attempt.Attempt - 1 && item.State == DeliveryStates.Failed)
                    .ExecuteUpdateAsync(item => item.SetProperty(row => row.State, DeliveryStates.Dead).SetProperty(row => row.NextRetryAt, (DateTimeOffset?)null), cancellationToken);
                WebhookLog.Skipped(logger, attempt.WebhookId, attempt.DeliveryId, attempt.Attempt);
                return null;
            }

            if (rows.Find(item => item.State == DeliveryStates.Pending) is { } busy)
            {
                if (!IsAbandoned(busy, now))
                {
                    // A burst to a receiver that hangs then wakes once per send instead of every few seconds.
                    var sent = busy.CreatedAt + options.Value.Timeout;
                    var at = sent > now + Busy ? sent : now + Busy;
                    await DeferAsync(attempt, webhook, at + (Busy * Random.Shared.NextDouble()), cancellationToken);
                    return null;
                }

                await TakeOverAsync(busy, webhook, cancellationToken);
            }

            var claim = new WebhookDelivery
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = attempt.DeliveryId,
                WebhookId = webhook.Id,
                EventId = attempt.EventId,
                Event = attempt.Event,
                Attempt = attempt.Attempt,
                State = DeliveryStates.Pending,
                RequestBody = attempt.Body,
                Manual = attempt.Manual,
                CreatedAt = now,
            };
            deliveries.Add(claim);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return (webhook, claim.Id);
            }
            catch (DbUpdateException) when (round < 3)
            {
                // Another job claimed this attempt or another of the webhook since the read, and the unique indexes refused
                // this one; the next round finds which.
                database.ChangeTracker.Clear();
            }
        }
    }

    // The worker that claimed the attempt stopped before it recorded the result, so whether the receiver got it is not
    // known. It counts as failed and the next attempt follows, scheduled first as in ExecuteAsync; it is not sent again.
    private async Task TakeOverAsync(WebhookDelivery abandoned, Webhook webhook, CancellationToken cancellationToken)
    {
        var retryAt = RetryAt(abandoned.Attempt, abandoned.Manual);
        if (retryAt is { } at)
        {
            var next = new WebhookAttempt(abandoned.WebhookId, abandoned.DeliveryId, abandoned.EventId, abandoned.Event, abandoned.Attempt + 1, abandoned.Manual, abandoned.RequestBody);
            await scheduler.ScheduleAsync<WebhookDeliveryJob, WebhookAttempt>(next, at, Options(webhook.SiteId), cancellationToken);
        }

        var state = retryAt is null ? DeliveryStates.Dead : DeliveryStates.Failed;
        var taken = await database.Set<WebhookDelivery>()
            .Where(item => item.Id == abandoned.Id && item.State == DeliveryStates.Pending)
            .ExecuteUpdateAsync(item => item.SetProperty(row => row.State, state).SetProperty(row => row.Error, Interrupted).SetProperty(row => row.NextRetryAt, retryAt), cancellationToken);
        if (taken == 1)
        {
            WebhookLog.TakenOver(logger, abandoned.WebhookId, abandoned.DeliveryId, abandoned.Attempt, abandoned.CreatedAt);
        }
    }

    private async Task DeferAsync(WebhookAttempt attempt, Webhook webhook, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await scheduler.ScheduleAsync<WebhookDeliveryJob, WebhookAttempt>(attempt, at, Options(webhook.SiteId), cancellationToken);
        WebhookLog.Deferred(logger, webhook.Id, attempt.DeliveryId, attempt.Attempt, at);
    }

    private DateTimeOffset? RetryAt(int attempt, bool manual) =>
        !manual && attempt <= Retries.Count ? time.GetUtcNow() + Retries[attempt - 1] : null;

    private DateTimeOffset AbandonedAt(WebhookDelivery pending) => pending.CreatedAt + options.Value.Timeout + AbandonMargin;

    private bool IsAbandoned(WebhookDelivery pending, DateTimeOffset now) => AbandonedAt(pending) <= now;

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
