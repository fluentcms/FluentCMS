using FluentCMS.Diagnostics;
using FluentCMS.Media;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Plugins.Media;

// Bound from FluentCms:Media. What is the instance's, since the storage is one for every site: where the local disk keeps
// the files, the largest request body an upload may send, and how transform URLs are signed and cached. The rules of
// uploads and the limits of transforms are each site's, in MediaSettings.
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

    // The Cache-Control of a transformed image.
    public string CacheControl { get; set; } = "public, max-age=86400";

    internal byte[]? SigningKeyBytes => string.IsNullOrEmpty(SigningKey) ? null : Convert.FromBase64String(SigningKey);

    internal bool IsValid() =>
        !string.IsNullOrWhiteSpace(Local?.Root)
        && MaxRequestBodySize > 0
        && (string.IsNullOrEmpty(SigningKey) || (Convert.TryFromBase64String(SigningKey, new byte[SigningKey.Length], out var length) && length >= MediaSigning.KeyLength))
        && !string.IsNullOrWhiteSpace(CacheControl) && CacheControlHeaderValue.TryParse(CacheControl, out _);
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
