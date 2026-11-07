using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Caching;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Core.Api.Delivery;

// A Delivery read with its cache headers. The ETag hashes the body, which holds the version and locale of every entry in
// it, populated ones included, and follows from the query, so it changes whenever what the caller reads does. There is no
// Last-Modified: no date in the body moves when an entry leaves a list, a populated target is unpublished or a role
// changes what the caller reads, so a 304 by date would keep a stale copy. A response the output cache is about to store
// is sent whole even to a matching If-None-Match, so that the copy exists for the next request.
internal sealed class DeliveryResponse : IResult
{
    public const string Vary = "Authorization, X-Api-Key, X-Cms-Site, X-Cms-Locale";

    private readonly JsonObject _body;
    private readonly SchemaType _type;
    private readonly bool _draft;

    private DeliveryResponse(JsonObject body, SchemaType type, bool draft)
    {
        _body = body;
        _type = type;
        _draft = draft;
    }

    public static DeliveryResponse Entry(JsonObject entry, SchemaType type, bool draft) => new(entry, type, draft);

    public static DeliveryResponse List(JsonObject list, SchemaType type, bool draft) => new(list, type, draft);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var services = httpContext.RequestServices;
        var json = services.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? JsonSerializerOptions.Web;
        var bytes = JsonSerializer.SerializeToUtf8Bytes<JsonNode>(_body, json);
        var etag = new EntityTagHeaderValue($"\"{Base64Url.EncodeToString(SHA256.HashData(bytes).AsSpan(0, 16))}\"");
        var api = ApiRequest.Of(httpContext);

        var response = httpContext.Response;
        response.GetTypedHeaders().ETag = etag;
        var options = services.GetRequiredService<DeliveryCacheOptions>();
        response.Headers.CacheControl = _draft ? "no-store" : api.Operation.Principal.IsAnonymous ? options.SharedCacheControl : options.PrivateCacheControl;
        response.Headers.Vary = Vary;

        var cache = httpContext.Features.Get<IOutputCacheFeature>()?.Context;
        if (cache is { EnableOutputCaching: true })
        {
            cache.Tags.Add(CacheTags.Type(_type, api.Operation.Site.Value));
            foreach (var entry in EntryIds(_body))
            {
                cache.Tags.Add(CacheTags.Entry(entry));
            }
        }

        if (cache is not { EnableOutputCaching: true, AllowCacheStorage: true } && Matches(httpContext.Request, etag))
        {
            response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength = bytes.Length;
        await response.Body.WriteAsync(bytes, httpContext.RequestAborted);
    }

    // Every entry in the body, populated targets and references included, so that a change to one evicts what shows it.
    private static HashSet<Guid> EntryIds(JsonNode body)
    {
        HashSet<Guid> ids = [];
        Stack<JsonNode> pending = new([body]);
        while (pending.TryPop(out var node))
        {
            if (node is JsonObject item)
            {
                if (item["id"] is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id))
                {
                    ids.Add(id);
                }

                foreach (var (_, child) in item)
                {
                    if (child is not null)
                    {
                        pending.Push(child);
                    }
                }
            }
            else if (node is JsonArray items)
            {
                foreach (var child in items)
                {
                    if (child is not null)
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        return ids;
    }

    private static bool Matches(HttpRequest request, EntityTagHeaderValue etag) =>
        request.Headers.IfNoneMatch.Count > 0
        && EntityTagHeaderValue.TryParseList(request.Headers.IfNoneMatch, out var tags)
        && tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(etag, useStrongComparison: false));
}
