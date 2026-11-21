using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.ApiKeys;

// The keys routes of the fake Management API, as the server answers them, with one key of the main site.
internal sealed class ApiKeysFake
{
    public const string Secret = "fcms_dk_SECRETabcdefghijklmnopqrstuvwxyz012345";
    public const string RotatedSecret = "fcms_dk_ROTATEDabcdefghijklmnopqrstuvwxyz0123";

    public static readonly Guid Reader = new("0193a1b2-0000-7000-8000-0000000000b1");
    public static readonly Guid KeyId = new("0193a1b2-0000-7000-8000-00000000c001");

    private readonly Lock gate = new();
    private readonly List<(HttpMethod Method, string Path, string? Body, bool Idempotent)> calls = [];

    // Answers a call before the routes below, when it gives one: a refusal of a test.
    public Func<HttpMethod, string, HttpResponseMessage?>? Refuse { get; set; }

    // A copy, since the handler adds to the list on the client's thread while a test reads it on its own.
    public IReadOnlyList<(HttpMethod Method, string Path, string? Body, bool Idempotent)> Calls
    {
        get
        {
            lock (gate)
            {
                return [.. calls];
            }
        }
    }

    public JsonObject Key { get; set; } = KeyOf(KeyId, "Shop reader", "fcms_dk_ab12");

    public JsonArray Keys { get; set; } = [];

    public JsonArray Roles { get; } =
    [
        new JsonObject { ["id"] = Reader, ["siteId"] = null, ["name"] = "Reader", ["description"] = null, ["builtIn"] = false, ["requireTwoFactor"] = false, ["grants"] = new JsonArray() },
    ];

    public static JsonObject KeyOf(Guid id, string name, string prefix) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["class"] = "delivery",
        ["keyPrefix"] = prefix,
        ["roleId"] = Reader,
        ["siteIds"] = new JsonArray(MainSite),
        ["typeNames"] = null,
        ["expiresAt"] = null,
        ["lastUsedAt"] = null,
        ["rateLimit"] = null,
        ["revokedAt"] = null,
        ["createdAt"] = "2026-11-01T10:00:00Z",
        ["createdBy"] = Guid.Empty,
        ["allowedAddresses"] = null,
        ["previousKeyExpiresAt"] = null,
    };

    public HttpResponseMessage? Respond(HttpRequestMessage request, string path, string? body)
    {
        if (!path.StartsWith("api-keys", StringComparison.Ordinal) && path != "roles")
        {
            return null;
        }

        lock (gate)
        {
            calls.Add((request.Method, path, body, request.Headers.Contains("Idempotency-Key")));
        }

        if (Refuse?.Invoke(request.Method, path) is { } refused)
        {
            return refused;
        }

        var keyPath = $"api-keys/{KeyId:D}";
        if (request.Method == HttpMethod.Get)
        {
            return path switch
            {
                "roles" => Json(new JsonObject { ["items"] = Roles.DeepClone() }),
                "api-keys" => Json(new JsonObject { ["items"] = Keys.Count > 0 ? Keys.DeepClone() : new JsonArray(Key.DeepClone()) }),
                _ when path == keyPath => Json(Key.DeepClone()),
                _ => Problem(HttpStatusCode.NotFound, "FCMS3015", "id"),
            };
        }

        if (request.Method == HttpMethod.Post && path == "api-keys")
        {
            return WithSecret(Key, Secret, HttpStatusCode.Created);
        }

        if (request.Method == HttpMethod.Post && path == keyPath + "/rotate")
        {
            Key["previousKeyExpiresAt"] = "2999-01-01T00:00:00Z";
            return WithSecret(Key, RotatedSecret, HttpStatusCode.OK);
        }

        if (request.Method == HttpMethod.Post && path == keyPath + "/expire")
        {
            Key["expiresAt"] = JsonNode.Parse(body ?? "{}")?["at"]?.DeepClone() ?? "2020-01-01T00:00:00Z";
            return Json(Key.DeepClone());
        }

        if (request.Method == HttpMethod.Delete && path == keyPath)
        {
            Key["revokedAt"] = "2026-11-02T10:00:00Z";
            return NoContent();
        }

        if (request.Method == HttpMethod.Put && path == keyPath + "/allowed-addresses")
        {
            Key["allowedAddresses"] = JsonNode.Parse(body!)!["allowedAddresses"]?.DeepClone();
            return Json(Key.DeepClone());
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    // A refusal with a validation error for each field and code given.
    public static HttpResponseMessage Invalid(params (string Path, string Code)[] errors) =>
        new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                new JsonObject
                {
                    ["status"] = 400,
                    ["code"] = "FCMS2100",
                    ["title"] = "Validation failed",
                    ["errors"] = new JsonArray([.. errors.Select(error => (JsonNode)new JsonObject { ["path"] = error.Path, ["code"] = error.Code, ["message"] = "Refused by the server" })]),
                }.ToJsonString(),
                Encoding.UTF8,
                "application/problem+json"),
        };

    private static HttpResponseMessage WithSecret(JsonObject key, string secret, HttpStatusCode status)
    {
        var answer = key.DeepClone().AsObject();
        answer["secret"] = secret;
        return new HttpResponseMessage(status) { Content = new StringContent(answer.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}

// Every message logged while a test runs, at every level.
internal sealed class LogCapture : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            capture.Messages.Enqueue(formatter(state, exception) + exception);
    }
}

internal static class SecretChecks
{
    // The fields of every component the page renders that hold the secret, by component and field: nothing of the page may
    // keep it once its dialog has closed.
    public static string[] Holders(IRenderedComponent<AdminApp> app, string secret) =>
        [.. app.FindComponents<IComponent>().SelectMany(component => FieldsHolding(component.Instance, secret))];

    // A button of the wrapped set by its whole text.
    public static AngleSharp.Dom.IElement Button(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    public static CmsSecretDialog SecretDialog(IRenderedComponent<AdminApp> app) => app.FindComponent<CmsSecretDialog>().Instance;

    private static IEnumerable<string> FieldsHolding(object component, string secret)
    {
        for (var type = component.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (field.GetValue(component) is { } value && (value as string ?? Describe(value))?.Contains(secret, StringComparison.Ordinal) == true)
                {
                    yield return $"{type.Name}.{field.Name}";
                }
            }
        }
    }

    // The text members of a record of the area, such as the answer of a create, which keeps its secret out of ToString.
    private static string? Describe(object value) =>
        value.GetType().Assembly != typeof(FluentCMS.Admin.Administration.ApiKeys.ApiKeysResources).Assembly ? null
            : string.Concat(value.GetType().GetProperties()
                .Where(property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
                .Select(property => property.GetValue(value) as string));
}
