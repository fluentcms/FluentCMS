using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Plugins.Webhooks.Admin.Api;

// A webhook as the routes answer it, public since WebhookForm takes one. Every header's value is RedactedValue; Secret is only in the answer of a create that let
// the server make it, and PreviousSecretExpiresAt only while the secret a rotation replaced still signs.
public sealed record WebhookRecord
{
    public required Guid Id { get; init; }

    public Guid SiteId { get; init; }

    public required string Name { get; init; }

    public required string Url { get; init; }

    public IReadOnlyList<string> Events { get; init; } = [];

    public IReadOnlyList<string> Types { get; init; } = [];

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public bool Enabled { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public Guid UpdatedBy { get; init; }

    public string? Secret { get; init; }

    public DateTimeOffset? PreviousSecretExpiresAt { get; init; }
}

// One attempt of a delivery; the attempts of a delivery share its DeliveryId. Request is the body sent, and Response the
// start of the receiver's answer.
internal sealed record WebhookDeliveryRecord
{
    public required Guid Id { get; init; }

    public required Guid DeliveryId { get; init; }

    public Guid EventId { get; init; }

    public required string Event { get; init; }

    public int Attempt { get; init; }

    public required string State { get; init; }

    public int? StatusCode { get; init; }

    public long DurationMs { get; init; }

    public string Request { get; init; } = "";

    public string? Response { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset? NextRetryAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed record WebhookDeliveryPage
{
    public required IReadOnlyList<WebhookDeliveryRecord> Items { get; init; }

    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

// The delivery a ping or a redelivery queued, which the log lists once it is attempted.
internal sealed record WebhookDeliveryQueued
{
    public required Guid DeliveryId { get; init; }
}

// What a create or a PUT sends: the whole webhook. A header whose value is RedactedValue keeps its stored value while the URL
// keeps its scheme, host and port; a Secret of null lets a create make one and an update keep the one stored.
internal sealed record WebhookDraft(
    string Name,
    string Url,
    IReadOnlyList<string> Events,
    IReadOnlyList<string> Types,
    IReadOnlyDictionary<string, string> Headers,
    bool Enabled,
    string? Secret = null);

// The calls of the half (spec 5.3), each of which needs webhooks.manage in the site the shell shows. None is about content,
// so none sends a content locale.
internal static class WebhooksApi
{
    public const string RedactedValue = "***";

    public const int PageSize = 20;

    public static async Task<IReadOnlyList<WebhookRecord>> GetWebhooksAsync(this IManagementApi api, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, "webhooks"), WebhooksJsonContext.Default.ItemListWebhookRecord, cancellationToken)).Value.Items;

    public static async Task<WebhookRecord> GetWebhookAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, Webhook(id)), WebhooksJsonContext.Default.WebhookRecord, cancellationToken)).Value;

    public static async Task<WebhookRecord> CreateWebhookAsync(this IManagementApi api, WebhookDraft draft, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Post, "webhooks", Body(draft)), WebhooksJsonContext.Default.WebhookRecord, cancellationToken)).Value;

    public static async Task<WebhookRecord> UpdateWebhookAsync(this IManagementApi api, Guid id, WebhookDraft draft, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Put, Webhook(id), Body(draft)), WebhooksJsonContext.Default.WebhookRecord, cancellationToken)).Value;

    public static Task DeleteWebhookAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.SendAsync(Request(HttpMethod.Delete, Webhook(id)), cancellationToken);

    // The attempts of the webhook's deliveries, newest first.
    public static async Task<WebhookDeliveryPage> GetDeliveriesAsync(this IManagementApi api, Guid id, int page, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, $"{Webhook(id)}/deliveries?page={page}&pageSize={PageSize}"), WebhooksJsonContext.Default.WebhookDeliveryPage, cancellationToken)).Value;

    public static async Task<Guid> RedeliverAsync(this IManagementApi api, Guid id, Guid deliveryId, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Post, $"{Webhook(id)}/deliveries/{deliveryId:D}/redeliver"), WebhooksJsonContext.Default.WebhookDeliveryQueued, cancellationToken)).Value.DeliveryId;

    public static async Task<Guid> PingAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Post, $"{Webhook(id)}/ping"), WebhooksJsonContext.Default.WebhookDeliveryQueued, cancellationToken)).Value.DeliveryId;

    // The webhook as it is, with only Enabled changed: its headers go back redacted to the same URL, which keeps their values.
    public static WebhookDraft Toggled(WebhookRecord webhook, bool enabled) =>
        new(webhook.Name, webhook.Url, webhook.Events, webhook.Types, webhook.Headers.ToDictionary(header => header.Key, _ => RedactedValue), enabled);

    private static string Webhook(Guid id) => $"webhooks/{id:D}";

    private static JsonObject Body(WebhookDraft draft)
    {
        var headers = new JsonObject();
        foreach (var (name, value) in draft.Headers)
        {
            headers[name] = value;
        }

        var body = new JsonObject
        {
            ["name"] = draft.Name,
            ["url"] = draft.Url,
            ["events"] = new JsonArray([.. draft.Events.Select(item => JsonValue.Create(item))]),
            ["types"] = new JsonArray([.. draft.Types.Select(item => JsonValue.Create(item))]),
            ["headers"] = headers,
            ["enabled"] = draft.Enabled,
        };
        if (draft.Secret is { } secret)
        {
            body["secret"] = secret;
        }

        return body;
    }

    private static ApiRequest Request(HttpMethod method, string path, JsonNode? body = null) => new(method, path) { Body = body, NoLocale = true };
}

// The answers of the routes the pages tell apart, by status, code and path only (spec 2.7).
internal static class WebhookProblems
{
    public const string InvalidField = "FCMS1560";
    public const string InvalidUrl = "FCMS1561";
    public const string PrivateAddress = "FCMS1562";
    public const string UnknownEvent = "FCMS1563";
    public const string NotFound = "FCMS1564";
    public const string DeliveryNotFound = "FCMS1565";
    public const string NameTaken = "FCMS1566";

    // What a call may throw: a problem, or a server that did not answer or answered nonsense.
    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem } && (problem.Code == code || problem.Errors.Any(error => error.Code == code));

    public static bool IsForbidden(Exception exception) => exception is ApiProblemException { Problem.Status: 403 };

    public static bool IsGone(Exception exception) => Has(exception, NotFound) || exception is ApiProblemException { Problem.Status: 404 };

    // The error of a member, as name, or of an item of it, as events[2] or headers.Authorization.
    public static ApiProblemError? FieldError(ApiProblem? problem, string member) =>
        problem?.Errors.FirstOrDefault(error => error.Path is { } path
            && (string.Equals(path, member, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(member + "[", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(member + ".", StringComparison.OrdinalIgnoreCase)));

    // The error of one header, whose name the server compares without case.
    public static ApiProblemError? HeaderError(ApiProblem? problem, string header) =>
        problem?.Errors.FirstOrDefault(error => string.Equals(error.Path, "headers." + header, StringComparison.OrdinalIgnoreCase));
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WebhookRecord))]
[JsonSerializable(typeof(ItemList<WebhookRecord>))]
[JsonSerializable(typeof(WebhookDeliveryPage))]
[JsonSerializable(typeof(WebhookDeliveryQueued))]
internal sealed partial class WebhooksJsonContext : JsonSerializerContext;
