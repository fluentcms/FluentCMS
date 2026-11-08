using FluentCMS.Diagnostics;
using FluentCMS.Media;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Plugins.Media;

// Bound from FluentCms:Media. What is the instance's, since the storage is one for every site: where the local disk keeps
// the files, the largest request body an upload may send, how transform URLs are signed and cached, and how long a deleted
// asset keeps its bytes. The rules of uploads and the limits of transforms are each site's, in MediaSettings.
public sealed class MediaOptions
{
    public const string Section = "FluentCms:Media";

    public LocalStorageOptions Local { get; set; } = new();

    // Bytes. A site's MaxRequestSize above it is cut to it.
    public long MaxRequestBodySize { get; set; } = 256L * 1024 * 1024;

    // False serves unsigned transform URLs, for a setup where every client is trusted.
    public bool RequireSignedUrls { get; set; } = true;

    // Base64 of at least 32 bytes. Set, it replaces the key the plugin generates; changing it invalidates every signed URL.
    public string? SigningKey { get; set; }

    // The Cache-Control of a transformed image and of the original file of a public asset.
    public string CacheControl { get; set; } = "public, max-age=86400";

    // How long after its delete the daily cleanup removes an asset's bytes and its row.
    public TimeSpan DeletedRetention { get; set; } = TimeSpan.FromDays(30);

    internal byte[]? SigningKeyBytes => string.IsNullOrEmpty(SigningKey) ? null : Convert.FromBase64String(SigningKey);

    internal bool IsValid() =>
        !string.IsNullOrWhiteSpace(Local?.Root)
        && MaxRequestBodySize > 0
        && (string.IsNullOrEmpty(SigningKey) || (Convert.TryFromBase64String(SigningKey, new byte[SigningKey.Length], out var length) && length >= MediaSigning.KeyLength))
        && !string.IsNullOrWhiteSpace(CacheControl) && CacheControlHeaderValue.TryParse(CacheControl, out _)
        && DeletedRetention > TimeSpan.Zero;
}

// The Cache-Control of the Delivery API, FluentCms:Delivery:Cache:CacheControl, which core checks, so that the media route
// says what content says: as written to a caller without credentials, private to one with them. With the output cache off,
// nothing is evicted, as nothing was stored.
internal sealed class DeliveryCacheControl
{
    public const string Section = "FluentCms:Delivery:Cache";

    private string? _private;

    public string CacheControl { get; set; } = "public, max-age=60, stale-while-revalidate=300";

    public bool OutputCache { get; set; } = true;

    public string Shared => CacheControl.Trim();

    public string Private => _private ??= PrivateOf(CacheControl);

    private static string PrivateOf(string cacheControl)
    {
        var value = CacheControlHeaderValue.Parse(cacheControl);
        value.Public = false;
        value.Private = true;
        value.SharedMaxAge = null;
        return value.ToString();
    }
}

public sealed class LocalStorageOptions
{
    // A path relative to the content root, or an absolute one.
    public string Root { get; set; } = "App_Data/media";
}

// The local disk is allowed in Production, and listed so that an operator knows the files are on this machine only; so are
// unsigned transform URLs, with which anyone can have the instance make any image within the sites' limits.
internal sealed class MediaInsecureDefaults(IFileStorage storage, IHostEnvironment environment, IOptions<MediaOptions> options) : IInsecureDefaultSource
{
    public const string LocalStorage = "media-local-storage";
    public const string UnsignedUrls = "media-unsigned-urls";

    public IEnumerable<InsecureDefault> GetInsecureDefaults()
    {
        if (!environment.IsProduction())
        {
            yield break;
        }

        if (storage is LocalFileStorage local)
        {
            yield return new InsecureDefault
            {
                Id = LocalStorage,
                Message = $"Media is stored on the local disk of this instance, in {local.Root}. Every instance needs that directory on one shared volume, and backups must include it; or use a storage provider such as S3 or Azure Blob.",
            };
        }

        if (!options.Value.RequireSignedUrls)
        {
            yield return new InsecureDefault
            {
                Id = UnsignedUrls,
                Message = $"{MediaOptions.Section}:RequireSignedUrls is false, so /media serves unsigned transform URLs and anyone can have this instance make variants of public images, within the limits of each site's media settings.",
            };
        }
    }
}
