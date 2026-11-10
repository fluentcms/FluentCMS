using FluentCMS.Core.Caching;
using FluentCMS.Core.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Core.Api.Delivery;

// FluentCms:Delivery:Cache. CacheControl is what browsers and CDNs are told; OutputCache and OutputCacheDuration are about
// the copy the server keeps.
public sealed class DeliveryCacheOptions
{
    public const string Section = "Delivery:Cache";
    public const string DefaultCacheControl = "public, max-age=60, stale-while-revalidate=300";

    private readonly Lazy<(string Shared, string Private)> _headers;

    public DeliveryCacheOptions() => _headers = new(Headers);

    // A published read without credentials gets it as written; one with a key or a token gets it as private.
    public string CacheControl { get; set; } = DefaultCacheControl;

    public bool OutputCache { get; set; } = true;

    public TimeSpan OutputCacheDuration { get; set; } = TimeSpan.FromSeconds(60);

    // The header that carries a published read's tags, for a CDN that purges by tag: Cache-Tag, Surrogate-Key or another
    // name. Empty, none is sent.
    public string? CacheTagHeader { get; set; }

    internal string? TagHeader => string.IsNullOrEmpty(CacheTagHeader) ? null : CacheTagHeader;

    // A CDN that is sent the tags purges by the CacheInvalidatedEvent, so it is raised while either needs it.
    internal bool Invalidates => OutputCache || TagHeader is not null;

    internal string SharedCacheControl => _headers.Value.Shared;

    // A CDN keys its copies by URL, not by credentials, so what a key or a token reads must not be stored there.
    internal string PrivateCacheControl => _headers.Value.Private;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(CacheControl) || !CacheControlHeaderValue.TryParse(CacheControl, out _))
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{nameof(CacheControl)} is '{CacheControl}'; it is not a Cache-Control value.");
        }

        if (OutputCacheDuration <= TimeSpan.Zero)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{nameof(OutputCacheDuration)} is {OutputCacheDuration}; it must be more than zero.");
        }

        if (TagHeader is { } header && !CacheTags.IsHeaderName(header))
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{nameof(CacheTagHeader)} is '{header}'; it is not a header name, or it is one the Delivery API writes itself.");
        }
    }

    private (string, string) Headers()
    {
        var value = CacheControlHeaderValue.Parse(CacheControl);
        value.Public = false;
        value.Private = true;
        value.SharedMaxAge = null;
        return (CacheControl.Trim(), value.ToString());
    }
}
