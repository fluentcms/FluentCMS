using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Webhooks;

// Event ids 9240 to 9259. A line names the webhook and the delivery, never its URL, secret or headers.
internal static partial class WebhookLog
{
    [LoggerMessage(EventId = 9240, Level = LogLevel.Information, Message = "Webhook {WebhookId} delivery {DeliveryId} attempt {Attempt} of {Event} succeeded with {StatusCode} in {DurationMs} ms.")]
    public static partial void Delivered(ILogger logger, Guid webhookId, Guid deliveryId, int attempt, string @event, int? statusCode, long durationMs);

    [LoggerMessage(EventId = 9241, Level = LogLevel.Warning, Message = "Webhook {WebhookId} delivery {DeliveryId} attempt {Attempt} of {Event} failed: {Error} The next attempt is at {RetryAt:u}.")]
    public static partial void Failed(ILogger logger, Guid webhookId, Guid deliveryId, int attempt, string @event, string? error, DateTimeOffset retryAt);

    [LoggerMessage(EventId = 9242, Level = LogLevel.Warning, Message = "Webhook {WebhookId} delivery {DeliveryId} attempt {Attempt} of {Event} failed, and no attempt follows: {Error}")]
    public static partial void Dead(ILogger logger, Guid webhookId, Guid deliveryId, int attempt, string @event, string? error);

    [LoggerMessage(EventId = 9243, Level = LogLevel.Information, Message = "Webhook {WebhookId} delivery {DeliveryId} attempt {Attempt} was not sent: the webhook is deleted or disabled.")]
    public static partial void Skipped(ILogger logger, Guid webhookId, Guid deliveryId, int attempt);

    [LoggerMessage(EventId = 9244, Level = LogLevel.Information, Message = "Webhook retention removed {Removed} attempts from the delivery log.")]
    public static partial void Pruned(ILogger logger, int removed);

    [LoggerMessage(EventId = 9245, Level = LogLevel.Error, Message = "Could not register the recurring retention of the webhook delivery log; old attempts stay until it is registered.")]
    public static partial void RetentionNotScheduled(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9246, Level = LogLevel.Debug, Message = "Webhook {WebhookId} delivery {DeliveryId} attempt {Attempt} waits until {RunAt:u}: an attempt to the webhook is in flight.")]
    public static partial void Deferred(ILogger logger, Guid webhookId, Guid deliveryId, int attempt, DateTimeOffset runAt);

    [LoggerMessage(EventId = 9247, Level = LogLevel.Warning, Message = "Webhook {WebhookId} delivery {DeliveryId} attempt {Attempt}, claimed at {ClaimedAt:u}, was never recorded and counts as failed.")]
    public static partial void TakenOver(ILogger logger, Guid webhookId, Guid deliveryId, int attempt, DateTimeOffset claimedAt);
}
