using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using FluentCMS.Events;
using FluentCMS.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Plugins.Media;

// The output cache tags of GET /api/delivery/v1/media/{id}: one per asset, which the events that change what the route
// answers evict through the outbox, and one per site for its presets, whose URLs the answers carry, which a preset's change
// evicts. Each eviction raises CacheInvalidatedEvent with its tags, as core's does, for a CDN to purge the same. An upload
// evicts nothing, since only a 200 is kept. Site is core's tag of the site, which core's policy adds to the stored copy.
internal sealed class MediaCache(IOutputCacheStore store, IEventBus events, IOptions<DeliveryCacheControl> delivery)
    : IOutboxEventHandler<MediaAssetUpdatedEvent>,
      IOutboxEventHandler<MediaAssetMovedEvent>,
      IOutboxEventHandler<MediaAssetReplacedEvent>,
      IOutboxEventHandler<MediaAssetDeletedEvent>
{
    public static string Asset(Guid id) => $"media:{id:D}";

    public static string Presets(Guid site) => $"media-presets:{site:D}";

    public static string Site(Guid site) => $"site:{site:D}";

    public ValueTask HandleAsync(MediaAssetUpdatedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => EvictAsync(message, cancellationToken);

    public ValueTask HandleAsync(MediaAssetMovedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => EvictAsync(message, cancellationToken);

    public ValueTask HandleAsync(MediaAssetReplacedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => EvictAsync(message, cancellationToken);

    public ValueTask HandleAsync(MediaAssetDeletedEvent message, EventDelivery delivery, CancellationToken cancellationToken) => EvictAsync(message, cancellationToken);

    public async ValueTask EvictAsync(string[] tags, CancellationToken cancellationToken)
    {
        if (!delivery.Value.Invalidates)
        {
            return;
        }

        foreach (var tag in tags)
        {
            await store.EvictByTagAsync(tag, cancellationToken);
        }

        await events.PublishAsync(new CacheInvalidatedEvent(tags), cancellationToken);
    }

    private ValueTask EvictAsync(MediaAssetEvent message, CancellationToken cancellationToken) => EvictAsync([Asset(message.AssetId)], cancellationToken);
}

// The answer of the Delivery route, with the cache headers of a content read (4.10): an ETag that hashes the body, no
// Last-Modified, the Cache-Control of the Delivery API, shared only for a public asset read without credentials, and the
// Vary of content. The tags go on a copy the output cache keeps, beside the site's that core adds, and with the site's in
// the tag header when one is configured. A response the output cache is about to store is sent whole even to a matching
// If-None-Match, so that the copy exists for the next request.
internal sealed class MediaDeliveryResponse(MediaDelivery delivered, Guid site, bool shared) : IResult
{
    public const string Vary = "Authorization, X-Api-Key, X-Cms-Site, X-Cms-Locale";

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var services = httpContext.RequestServices;
        var json = services.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? JsonSerializerOptions.Web;
        var asset = delivered.Asset;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new DeliveredAsset(asset.Id, asset.FileName, asset.MimeType, asset.Size, asset.Width, asset.Height, asset.Title, asset.Locale, asset.Alt, asset.Caption, asset.Credit, asset.Tags, asset.FocalPoint, asset.IsPrivate, asset.CreatedAt, asset.UpdatedAt, delivered.Urls),
            json);
        var etag = new EntityTagHeaderValue($"\"{Base64Url.EncodeToString(SHA256.HashData(bytes).AsSpan(0, 16))}\"");

        var response = httpContext.Response;
        response.GetTypedHeaders().ETag = etag;
        var cacheControl = services.GetRequiredService<IOptions<DeliveryCacheControl>>().Value;
        response.Headers.CacheControl = shared ? cacheControl.Shared : cacheControl.Private;
        response.Headers.Vary = Vary;

        var cache = httpContext.Features.Get<IOutputCacheFeature>()?.Context;
        if (cache is { EnableOutputCaching: true })
        {
            cache.Tags.Add(MediaCache.Asset(asset.Id));
            cache.Tags.Add(MediaCache.Presets(site));
        }

        if (cacheControl.TagHeader is { } header)
        {
            response.Headers[header] = cacheControl.TagHeaderValue([MediaCache.Site(site), MediaCache.Asset(asset.Id), MediaCache.Presets(site)]);
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

    private static bool Matches(HttpRequest request, EntityTagHeaderValue etag) =>
        request.Headers.IfNoneMatch.Count > 0
        && EntityTagHeaderValue.TryParseList(request.Headers.IfNoneMatch, out var tags)
        && tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(etag, useStrongComparison: false));

    // What a client reads of an asset: no site, folder, checksum or the ids of the people who uploaded and changed it.
    private sealed record DeliveredAsset(
        Guid Id,
        string FileName,
        string MimeType,
        long Size,
        int? Width,
        int? Height,
        string? Title,
        string Locale,
        string? Alt,
        string? Caption,
        string? Credit,
        IReadOnlyList<string> Tags,
        MediaFocalPoint? FocalPoint,
        bool IsPrivate,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        MediaUrls? Urls);
}
