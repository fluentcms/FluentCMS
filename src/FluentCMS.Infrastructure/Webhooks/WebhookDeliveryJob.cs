using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Models;
using FluentCMS.Core.Results;
using FluentCMS.Core.Security;
using FluentCMS.Core.Webhooks;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Webhooks;

// Sends one delivery and logs the attempt. A 2xx answer delivers it. Any other answer, a timeout or a network error is thrown, so the
// job queue retries it with its growing delay until the attempts run out (ADR-0010). What no retry can fix, such as a subscription
// disabled since or a host taken off the allow-list, ends it at once.
internal sealed class WebhookDeliveryJob(
    FluentCmsDbContext context,
    ISecretProtector protector,
    IOutboundHosts outbound,
    WebhookClient client,
    TimeProvider clock) : IJobHandler
{
    public string Type => WebhookDeliveries.JobType;

    public async Task<Result> RunAsync(JobContext job, CancellationToken cancellationToken)
    {
        if (WebhookDeliveries.Read(job.Job.Payload) is not { } delivery)
        {
            return Result.Failure("payload_invalid", "A webhook delivery names its subscription, delivery id, event and body.");
        }

        var subscription = await context.WebhookSubscriptions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == delivery.SubscriptionId, cancellationToken);
        if (subscription is null || subscription.DisabledAt is not null)
        {
            await LogAsync(delivery, job.Job.Attempts, null, "The webhook was disabled before this delivery was sent, so nothing was sent.", TimeSpan.Zero);
            return Result.Success();
        }

        var url = new Uri(subscription.Url, UriKind.Absolute);
        if (!outbound.IsAllowed(url))
        {
            var refused = $"The host {url.Host} is no longer on the outbound allow-list, so nothing was sent; an administrator adds it to Outbound__AllowedHosts.";
            await LogAsync(delivery, job.Job.Attempts, null, refused, TimeSpan.Zero);
            return Result.Failure(WebhookErrorCodes.HostNotAllowed, refused);
        }

        string secret;
        try
        {
            secret = protector.Unprotect(WebhookSecrets.ProtectionPurpose, subscription.ProtectedSecret);
        }
        catch (CryptographicException)
        {
            // Another attempt reads the same keys, so it would fail the same way.
            const string unreadable = "The signing secret could not be read, so nothing was sent; the Data Protection keys may have been replaced. Add the webhook again.";
            await LogAsync(delivery, job.Job.Attempts, null, unreadable, TimeSpan.Zero);
            return Result.Failure("webhook_secret_unreadable", unreadable);
        }

        var (status, error, duration) = await SendAsync(url, delivery, secret, cancellationToken);
        await LogAsync(delivery, job.Job.Attempts, status, error, duration);

        // Thrown so the job queue retries it; the log above already holds what happened.
        return error is null ? Result.Success() : throw new HttpRequestException(error);
    }

    private async Task<(int? Status, string? Error, TimeSpan Duration)> SendAsync(
        Uri url,
        WebhookDeliveries.DeliveryPayload delivery,
        string secret,
        CancellationToken cancellationToken)
    {
        var timestamp = clock.GetUtcNow();
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(delivery.Body, Encoding.UTF8, "application/json") };
        request.Headers.Add(WebhookSignatures.IdHeader, delivery.DeliveryId);
        request.Headers.Add(WebhookSignatures.TimestampHeader, WebhookSignatures.Timestamp(timestamp));
        request.Headers.Add(WebhookSignatures.SignatureHeader, WebhookSignatures.Sign(secret, delivery.DeliveryId, timestamp, delivery.Body));

        var started = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;

            return response.IsSuccessStatusCode
                ? (status, null, started.Elapsed)
                : (status, $"The endpoint answered {status}; only a 2xx answer counts as delivered.", started.Elapsed);
        }
        catch (HttpRequestException exception)
        {
            return (null, $"The request failed: {exception.Message}", started.Elapsed);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "The endpoint did not answer in time.", started.Elapsed);
        }
    }

    // In a save of its own, outside any unit of work, as a log: the attempt stays on record whatever happens to the job.
    private async Task LogAsync(WebhookDeliveries.DeliveryPayload delivery, int attempt, int? status, string? error, TimeSpan duration)
    {
        var now = clock.GetUtcNow();
        context.WebhookDeliveries.Add(new WebhookDeliveryRow
        {
            Id = Guid.CreateVersion7(now),
            SubscriptionId = delivery.SubscriptionId,
            DeliveryId = delivery.DeliveryId,
            Event = delivery.Event,
            ChangeSetId = delivery.ChangeSetId,
            Attempt = attempt,
            AttemptedAt = now,
            StatusCode = status,
            Error = error is { Length: > WebhookDeliveryConfiguration.MaxErrorLength } ? error[..WebhookDeliveryConfiguration.MaxErrorLength] : error,
            Duration = duration,
        });
        await context.SaveChangesAsync(CancellationToken.None);
    }
}
