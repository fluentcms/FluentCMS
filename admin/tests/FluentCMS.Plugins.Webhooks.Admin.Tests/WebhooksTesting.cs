using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Webhooks.Admin.Tests;

// The half's form and pages with the admin's services they inject: the webhooks of a site and their deliveries in memory
// behind IManagementApi, a schema of two types, and an announcer that keeps what it was given.
public abstract class WebhooksTestContext : BunitContext
{
    internal const string Secret = "whsec_0123456789abcdef0123456789abcdef";

    protected WebhooksTestContext()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddCmsComponents();
        Services.AddLocalization();
        Services.AddSingleton<IManagementApi>(Server);
        Services.AddSingleton<IAdminSchema>(new FakeSchema());
        Services.AddSingleton<IAnnouncer>(Announcer);
        Services.AddSingleton(TimeProvider.System);
    }

    internal FakeWebhooks Server { get; } = new();

    internal FakeAnnouncer Announcer { get; } = new();

    protected string CmsText(string name) => Services.GetRequiredService<ICmsTextSource>().GetText(name)!;

    protected static IElement Button<T>(IRenderedComponent<T> cut, string text)
        where T : IComponent =>
        cut.FindAll("fluent-button").First(button => button.TextContent.Trim() == text);

    protected static bool HasButton<T>(IRenderedComponent<T> cut, string text)
        where T : IComponent =>
        cut.FindAll("fluent-button").Any(button => button.TextContent.Trim() == text);

    protected static IElement Field<T>(IRenderedComponent<T> cut, string label)
        where T : IComponent
    {
        var found = cut.FindAll("label").First(element => element.TextContent.Trim().TrimEnd('*').TrimEnd() == label);
        return cut.Find("#" + found.GetAttribute("for"));
    }

    // The checkbox of the wrapped set whose label is the text.
    protected static IElement Box<T>(IRenderedComponent<T> cut, string label)
        where T : class, IComponent =>
        cut.FindComponents<CmsCheckbox>().Single(box => box.Instance.Label == label).Find("fluent-checkbox");

    // The message under a field, by its label.
    protected static string? Message<T>(IRenderedComponent<T> cut, string label)
        where T : IComponent
    {
        var field = Field(cut, label);
        return field.ParentElement!.QuerySelector(".cms-field-message")?.TextContent.Trim();
    }

    internal static ApiProblemException Problem(int status, string code, string? path = null) =>
        new(new ApiProblem { Status = status, Code = code, Errors = path is null ? [] : [new ApiProblemError { Path = path, Code = code, Message = "Refused." }] });
}

internal sealed class FakeAnnouncer : IAnnouncer
{
    public List<string> Messages { get; } = [];

    public void Announce(string message, AnnouncementPoliteness politeness = AnnouncementPoliteness.Polite) => Messages.Add(message);
}

internal sealed class FakeSchema : IAdminSchema
{
    public Task<SchemaDescription> GetSchemaAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SchemaDescription
    {
        Types = [Type("article", "Article"), Type("page", null)],
        Components = [],
        Blocks = [],
    });

    private static SchemaTypeDescription Type(string name, string? displayName) =>
        new() { Name = name, Kind = "collection", DisplayName = displayName, Fields = [] };
}

// The webhooks routes as the server answers them, over webhooks in memory. A header's value never comes back: an answer gives
// *** for each, and *** sent in a PUT keeps the stored value only while the URL keeps its scheme, host and port, else it is
// refused with FCMS1560 on headers.{name}. A create without a secret answers Secret. A ping or a redelivery queues a
// delivery that the next read of the log shows with the result Outcome gives it.
internal sealed class FakeWebhooks : IManagementApi
{
    public Dictionary<Guid, JsonObject> Webhooks { get; } = [];

    // The real header values, by webhook.
    public Dictionary<Guid, Dictionary<string, string>> Headers { get; } = [];

    public Dictionary<Guid, List<JsonObject>> Deliveries { get; } = [];

    public List<ApiRequest> Requests { get; } = [];

    public Func<ApiRequest, ApiProblemException?>? Refuse { get; set; }

    // The state, status code and error of a queued delivery once it is read.
    public (string State, int? Status, string? Error) Outcome { get; set; } = ("succeeded", 200, null);

    // How many reads of the log a queued delivery stays queued for.
    public int QueuedFor { get; set; }

    private readonly List<(Guid Webhook, JsonObject Delivery, int Reads)> queued = [];

    public Guid Add(string name, string url, string[] events, Dictionary<string, string>? headers = null, bool enabled = true)
    {
        var id = Guid.NewGuid();
        Webhooks[id] = new JsonObject
        {
            ["id"] = id,
            ["siteId"] = Guid.Empty,
            ["name"] = name,
            ["url"] = url,
            ["events"] = new JsonArray([.. events.Select(item => JsonValue.Create(item))]),
            ["types"] = new JsonArray(),
            ["enabled"] = enabled,
            ["createdAt"] = "2026-11-20T08:00:00+00:00",
            ["createdBy"] = Guid.Empty,
            ["updatedAt"] = "2026-11-20T08:00:00+00:00",
            ["updatedBy"] = Guid.Empty,
        };
        Headers[id] = headers ?? [];
        Deliveries[id] = [];
        return id;
    }

    public JsonObject AddDelivery(Guid webhook, string @event, string state, int? status, string? response = null, string? error = null, Guid? deliveryId = null, int attempt = 1)
    {
        var delivery = Delivery(@event, state, status, response, error, deliveryId ?? Guid.NewGuid(), attempt);
        Deliveries[webhook].Insert(0, delivery);
        return delivery;
    }

    public async Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return new ApiResponse<T>(HttpStatusCode.OK, null, Answer(request).Deserialize(answer)!);
    }

    public async Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Answer(request);
        return new ApiResponse(HttpStatusCode.NoContent, null);
    }

    private JsonObject Answer(ApiRequest request)
    {
        Requests.Add(request);
        if (Refuse?.Invoke(request) is { } refused)
        {
            throw refused;
        }

        var parts = request.Path.Split('?', 2);
        var segments = parts[0].Split('/');
        return (request.Method.Method, segments) switch
        {
            ("GET", ["webhooks"]) => new JsonObject { ["items"] = new JsonArray([.. Webhooks.Values.OrderBy(item => (string)item["name"]!, StringComparer.Ordinal).Select(Redacted)]) },
            ("POST", ["webhooks"]) => Create(request.Body!.AsObject()),
            ("GET", ["webhooks", var id]) => Redacted(Webhook(Guid.Parse(id))),
            ("PUT", ["webhooks", var id]) => Update(Guid.Parse(id), request.Body!.AsObject()),
            ("DELETE", ["webhooks", var id]) => Delete(Guid.Parse(id)),
            ("GET", ["webhooks", var id, "deliveries"]) => Log(Guid.Parse(id), parts[1]),
            ("POST", ["webhooks", var id, "ping"]) => Queue(Guid.Parse(id), "ping", "{\"event\":\"ping\"}"),
            ("POST", ["webhooks", var id, "deliveries", var delivery, "redeliver"]) => Redeliver(Guid.Parse(id), Guid.Parse(delivery)),
            _ => throw new InvalidOperationException($"No answer for {request.Method} {request.Path}."),
        };
    }

    private JsonObject Webhook(Guid id) =>
        Webhooks.TryGetValue(id, out var webhook) ? webhook : throw WebhooksTestContext.Problem(404, "FCMS1564");

    private JsonObject Redacted(JsonObject webhook)
    {
        var copy = webhook.DeepClone().AsObject();
        var headers = new JsonObject();
        foreach (var name in Headers[(Guid)webhook["id"]!].Keys)
        {
            headers[name] = "***";
        }

        copy["headers"] = headers;
        return copy;
    }

    private JsonObject Create(JsonObject body)
    {
        var id = Add(
            body["name"]!.GetValue<string>(),
            body["url"]!.GetValue<string>(),
            [.. body["events"]!.AsArray().Select(item => item!.GetValue<string>())],
            body["headers"]!.AsObject().ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>()),
            body["enabled"]!.GetValue<bool>());
        Webhooks[id]["types"] = body["types"]!.DeepClone();
        var answer = Redacted(Webhooks[id]);
        if (body["secret"] is null)
        {
            answer["secret"] = WebhooksTestContext.Secret;
        }

        return answer;
    }

    private JsonObject Update(Guid id, JsonObject body)
    {
        var webhook = Webhook(id);
        var stored = Headers[id];
        var url = body["url"]!.GetValue<string>();
        var sameOrigin = SameOrigin((string)webhook["url"]!, url);
        var headers = new Dictionary<string, string>();
        List<ApiProblemError> errors = [];
        foreach (var (name, node) in body["headers"]!.AsObject())
        {
            var value = node!.GetValue<string>();
            if (value == "***")
            {
                if (sameOrigin && stored.TryGetValue(name, out var kept))
                {
                    headers[name] = kept;
                }
                else
                {
                    errors.Add(new ApiProblemError { Path = $"headers.{name}", Code = "FCMS1560", Message = $"The header '{name}' needs its value." });
                }
            }
            else
            {
                headers[name] = value;
            }
        }

        if (errors.Count > 0)
        {
            throw new ApiProblemException(new ApiProblem { Status = 422, Code = "FCMS0001", Errors = errors });
        }

        foreach (var member in (string[])["name", "url", "events", "types", "enabled"])
        {
            webhook[member] = body[member]!.DeepClone();
        }

        Headers[id] = headers;
        if (body["secret"] is not null)
        {
            webhook["previousSecretExpiresAt"] = "2999-01-01T00:00:00+00:00";
        }

        return Redacted(webhook);
    }

    private JsonObject Delete(Guid id)
    {
        Webhook(id);
        Webhooks.Remove(id);
        Deliveries.Remove(id);
        return [];
    }

    private JsonObject Log(Guid id, string query)
    {
        Webhook(id);
        foreach (var item in queued.ToList())
        {
            queued.Remove(item);
            if (item.Reads < QueuedFor)
            {
                queued.Add(item with { Reads = item.Reads + 1 });
                continue;
            }

            item.Delivery["state"] = Outcome.State;
            item.Delivery["statusCode"] = Outcome.Status;
            item.Delivery["error"] = Outcome.Error;
            item.Delivery["response"] = Outcome.Status is null ? null : "ok";
        }

        var values = query.Split('&').Select(pair => pair.Split('=')).ToDictionary(pair => pair[0], pair => int.Parse(pair[1], CultureInfo.InvariantCulture));
        var (page, size) = (values["page"], values["pageSize"]);
        var all = Deliveries[id];
        return new JsonObject
        {
            ["items"] = new JsonArray([.. all.Skip((page - 1) * size).Take(size).Select(item => item.DeepClone())]),
            ["total"] = all.Count,
            ["page"] = page,
            ["pageSize"] = size,
        };
    }

    private JsonObject Queue(Guid id, string @event, string body)
    {
        Webhook(id);
        var delivery = Delivery(@event, "queued", null, null, null, Guid.NewGuid(), 1);
        delivery["request"] = body;
        Deliveries[id].Insert(0, delivery);
        queued.Add((id, delivery, 0));
        return new JsonObject { ["deliveryId"] = delivery["deliveryId"]!.DeepClone() };
    }

    private JsonObject Redeliver(Guid id, Guid deliveryId)
    {
        var sent = Deliveries[id].FirstOrDefault(item => (Guid)item["deliveryId"]! == deliveryId) ?? throw WebhooksTestContext.Problem(404, "FCMS1565");
        return Queue(id, (string)sent["event"]!, (string)sent["request"]!);
    }

    private static JsonObject Delivery(string @event, string state, int? status, string? response, string? error, Guid deliveryId, int attempt) => new()
    {
        ["id"] = Guid.NewGuid(),
        ["deliveryId"] = deliveryId,
        ["eventId"] = Guid.NewGuid(),
        ["event"] = @event,
        ["attempt"] = attempt,
        ["state"] = state,
        ["statusCode"] = status,
        ["durationMs"] = 42,
        ["request"] = $"{{\"version\":1,\"event\":\"{@event}\",\"data\":{{\"type\":\"article\"}}}}",
        ["response"] = response,
        ["error"] = error,
        ["nextRetryAt"] = null,
        ["createdAt"] = "2026-11-20T09:00:00+00:00",
    };

    private static bool SameOrigin(string stored, string url) =>
        Uri.TryCreate(stored, UriKind.Absolute, out var before) && Uri.TryCreate(url, UriKind.Absolute, out var after)
        && before.Scheme == after.Scheme && string.Equals(before.IdnHost, after.IdnHost, StringComparison.OrdinalIgnoreCase) && before.Port == after.Port;
}
