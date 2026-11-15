using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Host.Tests;

// The OpenAPI document of the reference host describes every Delivery and Management route of the first-party plugins, so
// a client generated from it has the types of their bodies. A route that answers nothing declares 204 with no content.
public sealed partial class PluginOpenApiTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    // The writes that read no body: their handlers take no body parameter, HttpRequest or HttpContext.
    private static readonly string[] TakeNoBody =
    [
        "post /api/management/v1/media/{id}/download-url",
        "post /api/management/v1/webhooks/{id}/deliveries/{delivery}/redeliver",
        "post /api/management/v1/webhooks/{id}/ping",
        "post /api/management/v1/mcp/clients/{id}/reject",
    ];

    // Handlers that take HttpRequest or HttpContext without Takes<T>, and why the document misses nothing they read.
    private static readonly Dictionary<string, string> ReadTheRequestOnlyForHeaders = new(StringComparer.Ordinal)
    {
        ["post /api/management/v1/ai/actions/{action}"] = "it reads Accept to choose the event stream; the body is its AiActionRequest parameter",
    };

    private readonly HostUnderTest _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task EveryRouteOfAFirstPartyPluginDeclaresItsBodies()
    {
        var app = await _host.StartAsync(new() { ["FluentCms:OpenApi:Enabled"] = "true" });
        using var client = _host.Client();
        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);
        var (status, document) = await HostUnderTest.ReadAsync(response);
        status.ShouldBe(HttpStatusCode.OK);
        var paths = document!["paths"]!.AsObject();

        var operations = PluginOperations(app.Services.GetRequiredService<EndpointDataSource>()).ToList();
        operations.Select(operation => operation.Plugin).Distinct()
            .ShouldBe(["FluentCMS.Plugins.AiAssist", "FluentCMS.Plugins.Mcp", "FluentCMS.Plugins.Media", "FluentCMS.Plugins.MultiSite", "FluentCMS.Plugins.Search", "FluentCMS.Plugins.Webhooks"], ignoreOrder: true);

        List<string> gaps = [];
        foreach (var (_, method, path, endpoint) in operations)
        {
            var name = $"{method} {path}";
            var handler = endpoint.Metadata.OfType<MethodInfo>().First();
            var readsTheRequest = handler.GetParameters().Any(parameter => parameter.ParameterType == typeof(HttpRequest) || parameter.ParameterType == typeof(HttpContext));

            // Takes<T> is the one IAcceptsMetadata that names no content type to routing; a bound body names application/json.
            var takes = endpoint.Metadata.OfType<IAcceptsMetadata>().Any(accepts => accepts.RequestType is not null && accepts.ContentTypes.Count == 0);
            if (readsTheRequest && !takes && !ReadTheRequestOnlyForHeaders.ContainsKey(name))
            {
                gaps.Add($"{name} reads the request by itself and declares no Takes<T>");
            }

            if (TakeNoBody.Contains(name) && (readsTheRequest || endpoint.Metadata.OfType<IAcceptsMetadata>().Any()))
            {
                gaps.Add($"{name} is listed as taking no body but its handler can read one");
            }

            if (paths[path]?[method] is not { } operation)
            {
                gaps.Add($"{name} is not in the document");
                continue;
            }

            var successes = operation["responses"]!.AsObject().Where(pair => pair.Key.StartsWith('2')).ToList();
            if (successes.Count == 0)
            {
                gaps.Add($"{name} declares no success");
            }

            foreach (var (code, answer) in successes)
            {
                var described = HasSchema(answer?["content"]);
                if (code == "204" ? answer?["content"] is not null : !described)
                {
                    gaps.Add(code == "204" ? $"{name} declares a body on 204" : $"{name} declares {code} with no body");
                }
            }

            var writes = method is "post" or "put" or "patch";
            if (writes && !TakeNoBody.Contains(name) && !HasSchema(operation["requestBody"]?["content"]))
            {
                gaps.Add($"{name} declares no request body");
            }
        }

        var names = operations.Select(operation => $"{operation.Method} {operation.Path}").ToHashSet(StringComparer.Ordinal);
        gaps.AddRange(TakeNoBody.Concat(ReadTheRequestOnlyForHeaders.Keys).Where(listed => !names.Contains(listed)).Select(listed => $"{listed} is listed but not mapped"));
        gaps.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheMediaUploadAndReplaceDeclareAFormAndTheQueuedDeliveriesTheirId()
    {
        await _host.StartAsync(new() { ["FluentCms:OpenApi:Enabled"] = "true" });
        using var client = _host.Client();
        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);
        var (_, document) = await HostUnderTest.ReadAsync(response);
        var paths = document!["paths"]!;
        var schemas = document["components"]!["schemas"]!;

        var upload = paths["/api/management/v1/media"]!["post"]!;
        upload["requestBody"]!["content"]!.AsObject().Select(pair => pair.Key).ShouldBe(["multipart/form-data"]);
        upload["responses"]!["201"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/MediaAssetList");
        schemas["MediaUploadForm"]!["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["files"]);
        paths["/api/management/v1/media/{id}/replace"]!["post"]!["requestBody"]!["content"]!["multipart/form-data"].ShouldNotBeNull();
        schemas["IFormFile"]!.ToJsonString().ShouldBe("""{"type":"string","format":"binary"}""");

        var preset = paths["/api/management/v1/media/presets/{name}"]!["put"]!;
        preset["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/MediaImageOptions");

        var ping = paths["/api/management/v1/webhooks/{id}/ping"]!["post"]!;
        ping["responses"]!["202"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/WebhookDeliveryQueued");
        schemas["WebhookDeliveryQueued"]!["properties"]!.AsObject().Select(pair => pair.Key).ShouldBe(["deliveryId"]);

        paths["/api/delivery/v1/media/{id}"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>()
            .ShouldBe("#/components/schemas/DeliveredAsset");
        paths["/api/management/v1/ai/actions/{action}"]!["post"]!["description"]!.GetValue<string>().ShouldContain("text/event-stream");
    }

    // The routes under /api that a plugin's own code maps, by the method that handles them, as the document names their paths.
    private static IEnumerable<(string Plugin, string Method, string Path, RouteEndpoint Endpoint)> PluginOperations(EndpointDataSource endpoints)
    {
        foreach (var endpoint in endpoints.Endpoints.OfType<RouteEndpoint>())
        {
            var plugin = endpoint.Metadata.OfType<MethodInfo>().Select(method => method.DeclaringType?.Assembly.GetName().Name).FirstOrDefault();
            var pattern = "/" + (endpoint.RoutePattern.RawText ?? "").Trim('/');
            if (plugin?.StartsWith("FluentCMS.Plugins.", StringComparison.Ordinal) != true || !pattern.StartsWith("/api/", StringComparison.Ordinal))
            {
                continue;
            }

            var path = Parameter().Replace(pattern, match => "{" + match.Groups[1].Value + "}");
            foreach (var method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
            {
                if (!HttpMethods.IsHead(method))
                {
                    yield return (plugin, method.ToLowerInvariant(), path, endpoint);
                }
            }
        }
    }

    private static bool HasSchema(JsonNode? content) =>
        content is JsonObject types && types.Count > 0 && types.All(pair => pair.Value?["schema"] is not null);

    // A route parameter without its constraint, so {id:guid} is {id}.
    [GeneratedRegex(@"\{([^}:=?*]+)[^}]*\}")]
    private static partial Regex Parameter();
}
