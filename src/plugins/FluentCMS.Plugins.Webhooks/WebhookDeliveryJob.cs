using System.Data.Common;
using FluentCMS.Content;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Webhooks;

// One attempt of a delivery. Body is the exact JSON every attempt sends. A manual one, a ping or a redelivery, is tried
// once and is sent even when the webhook is disabled. OccurredAt, the time of the event, orders the webhook's queue; a job
// enqueued without it queues at the time it runs.
internal sealed record WebhookAttempt(Guid WebhookId, Guid DeliveryId, Guid EventId, string Event, int Attempt, bool Manual, string Body, DateTimeOffset? OccurredAt = null);

// Queues an attempt in its webhook's queue and claims it with a pending row before sending it, so that a repeat of the job or
// a second job for the attempt sends nothing, and so that one attempt of a webhook is sent at a time, in the order they were
// queued. Then it records the result, schedules the next attempt itself, on the plugin's schedule rather than the runner's
// backoff, so the job fails only when the database does, and wakes the job of the attempt that heads the queue. A delivery
// that dies raises WebhookDeadEvent, and may disable its webhook (DisableAfter).
internal sealed class WebhookDeliveryJob(
    PluginDbContext<WebhooksPlugin> database,
    WebhookSender sender,
    WebhookSigning signing,
    IJobScheduler scheduler,
    IEventBus events,
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
        var recorded = await database.Set<WebhookDelivery>()
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

        // A row that another job took over in the meantime is that job's to report. Not cancelled, since a run again finds
        // the row recorded and would report nothing.
        if (state == DeliveryStates.Dead && recorded == 1 && !attempt.Manual)
        {
            await DiedAsync(webhook, attempt.DeliveryId, attempt.Event, attempt.Attempt, CancellationToken.None);
        }

        await NextAsync(webhook, claim, wake: true, cancellationToken);
    }

    // The webhook and the id of the claimed row, or null when this job sends nothing: the attempt is recorded, in flight or
    // waiting in the webhook's queue, or the webhook is gone or disabled.
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

            var own = await deliveries.AsNoTracking().FirstOrDefaultAsync(item => item.DeliveryId == attempt.DeliveryId && item.Attempt == attempt.Attempt, cancellationToken);
            if (own is null)
            {
                if (!webhook.Enabled && !attempt.Manual)
                {
                    await EndAsync(attempt, cancellationToken);
                    return null;
                }

                // Every attempt joins its webhook's queue first, ordered by the time of its event.
                own = new WebhookDelivery
                {
                    Id = Guid.CreateVersion7(),
                    DeliveryId = attempt.DeliveryId,
                    WebhookId = webhook.Id,
                    EventId = attempt.EventId,
                    Event = attempt.Event,
                    Attempt = attempt.Attempt,
                    State = DeliveryStates.Queued,
                    RequestBody = attempt.Body,
                    Manual = attempt.Manual,
                    CreatedAt = attempt.OccurredAt is { } occurred && occurred < time.GetUtcNow() ? occurred : time.GetUtcNow(),
                };
                deliveries.Add(own);
                try
                {
                    await database.SaveChangesAsync(cancellationToken);
                    database.ChangeTracker.Clear();
                }
                catch (DbUpdateException) when (round < 3)
                {
                    // A second job for the attempt queued it since the read; the next round finds it.
                    database.ChangeTracker.Clear();
                    continue;
                }
            }
            else if (own.State is not (DeliveryStates.Pending or DeliveryStates.Queued))
            {
                // Recorded. The job that recorded it woke the next, or the queue job does.
                return null;
            }
            else if (own.State == DeliveryStates.Pending)
            {
                // A repeat of a job whose attempt is claimed, or a second job for the attempt. A claim that is still pending
                // may be this job's own, left by a crash or a failed write, which nothing else finishes.
                if (!IsAbandoned(own, time.GetUtcNow()))
                {
                    await DeferAsync(attempt, webhook, AbandonedAt(own), cancellationToken);
                    return null;
                }

                await TakeOverAsync(own, webhook, cancellationToken);
                return await NextAsync(webhook, own.Id, wake: true, cancellationToken);
            }

            return await NextAsync(webhook, own.Id, wake: false, cancellationToken);
        }
    }

    // Claims the row own when it heads the webhook's queue and no attempt of the webhook is in flight. A job that ended an
    // attempt wakes the next: it enqueues the head's job, because only the job of an attempt sends it, so that the runner's
    // retry of that job after a crash finds its claim. A job that queued its attempt behind another row wakes nobody: the
    // head was queued before it, so the head's own job or the job that ended the last attempt saw it. A head that has
    // stalled is woken by the next attempt queued behind it, or by WebhookQueueJob.
    private async Task<(Webhook Webhook, Guid Claim)?> NextAsync(Webhook webhook, Guid own, bool wake, CancellationToken cancellationToken)
    {
        var deliveries = database.Set<WebhookDelivery>();
        while (true)
        {
            // The head first: a claim of own between the two reads then shows as busy, where the other order would show
            // neither a head nor an attempt in flight, and the job would end without waiting for the claim (#3416).
            var head = await Head(deliveries.AsNoTracking(), webhook.Id).FirstOrDefaultAsync(cancellationToken);
            var busy = await deliveries.AsNoTracking().FirstOrDefaultAsync(item => item.WebhookId == webhook.Id && item.State == DeliveryStates.Pending, cancellationToken);
            if (busy is not null && IsAbandoned(busy, time.GetUtcNow()))
            {
                await TakeOverAsync(busy, webhook, cancellationToken);
                continue;
            }

            if (busy is not null)
            {
                if (busy.Id == own)
                {
                    // Another job for this attempt claimed it since the read.
                    await DeferAsync(AttemptOf(busy), webhook, AbandonedAt(busy), cancellationToken);
                }

                return null;
            }

            if (head is null)
            {
                return null;
            }

            if (!webhook.Enabled && !head.Manual)
            {
                if (await deliveries.Where(item => item.Id == head.Id && item.State == DeliveryStates.Queued).ExecuteDeleteAsync(cancellationToken) == 1)
                {
                    await EndAsync(AttemptOf(head), cancellationToken);
                }

                continue;
            }

            if (head.Id != own)
            {
                if (!wake && !await WebhookQueueJob.HasStalledAsync(deliveries.AsNoTracking(), webhook.Id, head, time.GetUtcNow(), cancellationToken))
                {
                    return null;
                }

                await scheduler.EnqueueAsync<WebhookDeliveryJob, WebhookAttempt>(AttemptOf(head), Options(webhook.SiteId), cancellationToken);
                return null;
            }

            try
            {
                var claimed = await deliveries
                    .Where(item => item.Id == own && item.State == DeliveryStates.Queued)
                    .ExecuteUpdateAsync(item => item.SetProperty(row => row.State, DeliveryStates.Pending).SetProperty(row => row.CreatedAt, time.GetUtcNow()), cancellationToken);
                if (claimed == 1)
                {
                    return (webhook, own);
                }
            }
            catch (DbException)
            {
                // Another attempt of the webhook was claimed since the read, and ux_cms_webhooks_delivery_in_flight refused
                // this one. The next round finds it.
                if (!await deliveries.AnyAsync(item => item.WebhookId == webhook.Id && item.State == DeliveryStates.Pending, CancellationToken.None))
                {
                    throw;
                }
            }
        }
    }

    // The attempt before this one said another would follow; now none does, since the webhook is disabled.
    private async Task EndAsync(WebhookAttempt attempt, CancellationToken cancellationToken)
    {
        await database.Set<WebhookDelivery>()
            .Where(item => item.DeliveryId == attempt.DeliveryId && item.Attempt == attempt.Attempt - 1 && item.State == DeliveryStates.Failed)
            .ExecuteUpdateAsync(item => item.SetProperty(row => row.State, DeliveryStates.Dead).SetProperty(row => row.NextRetryAt, (DateTimeOffset?)null), cancellationToken);
        WebhookLog.Skipped(logger, attempt.WebhookId, attempt.DeliveryId, attempt.Attempt);
    }

    // The queued attempts of the webhook, head first, in the order of ix_cms_webhooks_delivery_queued.
    internal static IQueryable<WebhookDelivery> Head(IQueryable<WebhookDelivery> deliveries, Guid webhook) =>
        deliveries.Where(item => item.WebhookId == webhook && item.State == DeliveryStates.Queued).OrderBy(item => item.CreatedAt).ThenBy(item => item.Id);

    internal static WebhookAttempt AttemptOf(WebhookDelivery row) =>
        new(row.WebhookId, row.DeliveryId, row.EventId, row.Event, row.Attempt, row.Manual, row.RequestBody, row.State == DeliveryStates.Queued ? row.CreatedAt : null);

    // The worker that claimed the attempt stopped before it recorded the result, so whether the receiver got it is not
    // known. It counts as failed and the next attempt follows, scheduled first as in ExecuteAsync; it is not sent again.
    private async Task TakeOverAsync(WebhookDelivery abandoned, Webhook webhook, CancellationToken cancellationToken)
    {
        var retryAt = RetryAt(abandoned.Attempt, abandoned.Manual);
        if (retryAt is { } at)
        {
            var next = new WebhookAttempt(abandoned.WebhookId, abandoned.DeliveryId, abandoned.EventId, abandoned.Event, abandoned.Attempt + 1, abandoned.Manual, abandoned.RequestBody, abandoned.CreatedAt);
            await scheduler.ScheduleAsync<WebhookDeliveryJob, WebhookAttempt>(next, at, Options(webhook.SiteId), cancellationToken);
        }

        var state = retryAt is null ? DeliveryStates.Dead : DeliveryStates.Failed;
        var taken = await database.Set<WebhookDelivery>()
            .Where(item => item.Id == abandoned.Id && item.State == DeliveryStates.Pending)
            .ExecuteUpdateAsync(item => item.SetProperty(row => row.State, state).SetProperty(row => row.Error, Interrupted).SetProperty(row => row.NextRetryAt, retryAt), cancellationToken);
        if (taken == 1)
        {
            WebhookLog.TakenOver(logger, abandoned.WebhookId, abandoned.DeliveryId, abandoned.Attempt, abandoned.CreatedAt);
            if (state == DeliveryStates.Dead && !abandoned.Manual)
            {
                await DiedAsync(webhook, abandoned.DeliveryId, abandoned.Event, abandoned.Attempt, CancellationToken.None);
            }
        }
    }

    private async Task DiedAsync(Webhook webhook, Guid deliveryId, string name, int attempts, CancellationToken cancellationToken)
    {
        await events.PublishAsync(new WebhookDeadEvent(webhook.Id, webhook.Name, deliveryId, name, attempts), cancellationToken);
        if (options.Value.DisableAfter is not { } after || await FailingSinceAsync(webhook, cancellationToken) is not { } since)
        {
            return;
        }

        var now = time.GetUtcNow();
        if (since > now - after)
        {
            return;
        }

        // Only a webhook still enabled, so that of two deliveries that die at once one reports it.
        var disabled = await database.Set<Webhook>()
            .Where(item => item.Id == webhook.Id && item.Enabled)
            .ExecuteUpdateAsync(item => item.SetProperty(row => row.Enabled, false).SetProperty(row => row.UpdatedAt, now).SetProperty(row => row.UpdatedBy, Principal.System.Id), cancellationToken);
        if (disabled == 1)
        {
            WebhookLog.Disabled(logger, webhook.Id, since);
            await events.PublishAsync(new WebhookDisabledEvent(webhook.Id, webhook.Name, since), cancellationToken);
        }
    }

    // The claim of the first attempt that failed after the webhook's last success and its last change, so that a webhook
    // turned back on, or pointed somewhere else, starts afresh; null while its last attempt succeeded. A ping or a
    // redelivery counts like any other attempt.
    private async Task<DateTimeOffset?> FailingSinceAsync(Webhook webhook, CancellationToken cancellationToken)
    {
        var attempts = database.Set<WebhookDelivery>().AsNoTracking().Where(item => item.WebhookId == webhook.Id);
        var failures = attempts.Where(item => (item.State == DeliveryStates.Failed || item.State == DeliveryStates.Dead) && item.CreatedAt >= webhook.UpdatedAt);
        if (await attempts.Where(item => item.State == DeliveryStates.Succeeded).OrderByDescending(item => item.CreatedAt).Select(item => (DateTimeOffset?)item.CreatedAt).FirstOrDefaultAsync(cancellationToken) is { } succeeded)
        {
            failures = failures.Where(item => item.CreatedAt > succeeded);
        }

        return await failures.OrderBy(item => item.CreatedAt).Select(item => (DateTimeOffset?)item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
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
        if (signing.TryUnprotect(webhook.Secret) is not { } secret)
        {
            return new WebhookResult(false, null, 0, null, "The secret of the webhook could not be read; set a new one.");
        }

        if (signing.TryUnprotectHeaders(webhook.Headers) is not { } headers)
        {
            return new WebhookResult(false, null, 0, null, "The headers of the webhook could not be read; set them again.");
        }

        // Until the overlap after a rotation ends, the secret it replaced signs too. One that cannot be read is left out.
        List<string> secrets = [secret];
        if (webhook.PreviousSecret is { } previous && webhook.PreviousSecretExpiresAt > time.GetUtcNow() && signing.TryUnprotect(previous) is { } old && old != secret)
        {
            secrets.Add(old);
        }

        return await sender.SendAsync(webhook.Url, secrets, headers, attempt, cancellationToken);
    }
}
