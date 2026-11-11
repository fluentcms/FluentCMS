using FluentCMS.Content;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.AiAssist;

// Event ids 9360 to 9379. A line names the action, the field and the caller, and never holds the text, the suggestion,
// a key or the message of a provider's failure, which may repeat what the provider was sent.
internal static partial class AiLog
{
    [LoggerMessage(EventId = 9360, Level = LogLevel.Information, Message = "AI action {Action} on {Type}.{Field} by {PrincipalKind} {PrincipalId} in site {SiteId} through {Provider} {Model}: {Outcome} {Code} in {DurationMs} ms, {PromptTokens} prompt and {CompletionTokens} completion tokens.")]
    public static partial void Called(ILogger logger, string action, string type, string field, PrincipalKind principalKind, Guid principalId, Guid siteId, string provider, string model, string outcome, string? code, long durationMs, long promptTokens, long completionTokens);

    [LoggerMessage(EventId = 9361, Level = LogLevel.Warning, Message = "The AI provider {Provider} failed the action {Action} in site {SiteId} with {Failure}, status {Status}.")]
    public static partial void ProviderFailed(ILogger logger, string provider, string action, Guid siteId, string failure, int? status);

    [LoggerMessage(EventId = 9362, Level = LogLevel.Information, Message = "AI usage retention removed {Removed} rows from the usage log.")]
    public static partial void Pruned(ILogger logger, int removed);

    [LoggerMessage(EventId = 9363, Level = LogLevel.Error, Message = "Could not register the recurring retention of the AI usage log; old rows stay until it is registered.")]
    public static partial void RetentionNotScheduled(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9364, Level = LogLevel.Warning, Message = "{PrincipalKind} {PrincipalId} is over the {Limit} AI calls a minute of site {SiteId}, so its calls are refused for {RetryAfterSeconds} s. Its next refusals are not logged for a minute.")]
    public static partial void CallLimitExceeded(ILogger logger, PrincipalKind principalKind, Guid principalId, int limit, Guid siteId, int retryAfterSeconds);

    [LoggerMessage(EventId = 9365, Level = LogLevel.Error, Message = "Could not write the usage row of the AI action {Action} by {PrincipalKind} {PrincipalId} in site {SiteId}.")]
    public static partial void UsageNotRecorded(ILogger logger, Exception exception, string action, PrincipalKind principalKind, Guid principalId, Guid siteId);

    [LoggerMessage(EventId = 9366, Level = LogLevel.Warning, Message = "Site {SiteId} names an AI endpoint on the host {Host} that is not one of FluentCms:Ai:AllowedEndpoints, so its AI calls are refused.")]
    public static partial void EndpointRefused(ILogger logger, Guid siteId, string host);

    [LoggerMessage(EventId = 9367, Level = LogLevel.Error, Message = "The AI action {Action} failed while it streamed with {Failure}, answered as {Code}.")]
    public static partial void StreamFailed(ILogger logger, string action, string failure, string code);
}
