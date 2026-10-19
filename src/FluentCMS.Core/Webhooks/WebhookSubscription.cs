using FluentCMS.Core.Identity;
using FluentCMS.Core.Models;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Webhooks;

// One endpoint and the events it is told about (API-05). Its URL is checked when it is added: HTTPS on a host the administrator
// allow-listed (SEC-05). A subscription is never edited; it is disabled and added again, so its secret and URL never drift apart.
public sealed class WebhookSubscription
{
    public const int MaxUrlLength = 2048;

    private WebhookSubscription(Guid id, Uri url, IReadOnlyList<string> events, DateTimeOffset addedAt, UserInitiator addedBy, DateTimeOffset? disabledAt)
    {
        Id = id;
        Url = url;
        Events = events;
        AddedAt = addedAt;
        AddedBy = addedBy;
        DisabledAt = disabledAt;
    }

    public Guid Id { get; }

    public Uri Url { get; }

    public IReadOnlyList<string> Events { get; }

    public DateTimeOffset AddedAt { get; }

    public UserInitiator AddedBy { get; }

    public DateTimeOffset? DisabledAt { get; private set; }

    public bool Enabled => DisabledAt is null;

    public static Result<WebhookSubscription> Add(NewWebhookSubscription subscription, IOutboundHosts outbound, UserInitiator addedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(outbound);
        ArgumentNullException.ThrowIfNull(addedBy);

        var errors = new List<Error>();
        var url = CheckUrl(subscription.Url, outbound, errors);
        var events = CheckEvents(subscription.Events, errors);

        return errors.Count == 0
            ? Result<WebhookSubscription>.Success(new WebhookSubscription(Guid.CreateVersion7(now), url!, events, now.ToUniversalTime(), addedBy, disabledAt: null))
            : Result<WebhookSubscription>.Failure(errors);
    }

    // Rebuilds a stored subscription without checking it again, so it stays readable if the allow-list or the rules change.
    public static WebhookSubscription Restore(Guid id, Uri url, IReadOnlyList<string> events, DateTimeOffset addedAt, UserInitiator addedBy, DateTimeOffset? disabledAt)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(addedBy);

        return new WebhookSubscription(id, url, events, addedAt, addedBy, disabledAt);
    }

    public Result Disable(DateTimeOffset now)
    {
        if (DisabledAt is { } disabledAt)
        {
            return Result.Failure(WebhookErrorCodes.AlreadyDisabled, $"The webhook to {Url} was already disabled at {disabledAt:O}.");
        }

        DisabledAt = now.ToUniversalTime();
        return Result.Success();
    }

    private static Uri? CheckUrl(string? text, IOutboundHosts outbound, List<Error> errors)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length > MaxUrlLength)
        {
            errors.Add(new Error(WebhookErrorCodes.UrlTooLong, $"A webhook URL is at most {MaxUrlLength} characters, and this one has {value.Length}."));
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http") || url.Host.Length == 0)
        {
            errors.Add(new Error(
                WebhookErrorCodes.UrlInvalid,
                $"'{value}' is not a web address; give the full URL the endpoint listens on, such as https://hooks.example.com/fluentcms."));
            return null;
        }

        var failed = errors.Count;
        if (url.UserInfo.Length > 0)
        {
            errors.Add(new Error(
                WebhookErrorCodes.UrlHasCredentials,
                "A webhook URL cannot carry a user name or password, which would be stored and listed with it; the endpoint checks each delivery's signature instead."));
        }

        // Plain HTTP stays on this machine, for an endpoint under development.
        if (url.Scheme != Uri.UriSchemeHttps && !url.IsLoopback)
        {
            errors.Add(new Error(
                WebhookErrorCodes.UrlNotHttps,
                $"Deliveries carry content, so the URL uses https; plain http is accepted only for a local endpoint such as http://localhost:5005, and {url.Host} is not one."));
        }

        if (!outbound.IsAllowed(url))
        {
            errors.Add(new Error(
                WebhookErrorCodes.HostNotAllowed,
                $"The host {url.Host} is not on the outbound allow-list; an administrator adds it to Outbound__AllowedHosts, then the webhook can be added."));
        }

        return errors.Count == failed ? url : null;
    }

    // Kept in the order WebhookEvents lists them, so the same choice is always stored the same way.
    private static IReadOnlyList<string> CheckEvents(IReadOnlyList<string>? events, List<Error> errors)
    {
        var names = (events ?? []).Select(name => name?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
        var known = string.Join(", ", WebhookEvents.All);

        if (names.Count == 0)
        {
            errors.Add(new Error(WebhookErrorCodes.EventsMissing, $"A webhook needs at least one event to be told about: {known}."));
        }

        foreach (var unknown in names.Where(name => !WebhookEvents.All.Contains(name, StringComparer.Ordinal)))
        {
            errors.Add(new Error(WebhookErrorCodes.EventUnknown, $"'{unknown}' is not a webhook event; use {known}."));
        }

        return [.. WebhookEvents.All.Where(name => names.Contains(name, StringComparer.Ordinal))];
    }
}
