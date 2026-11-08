using FluentCMS.Diagnostics;
using FluentCMS.Media;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Plugins.Media;

// Bound from FluentCms:Media. What is the instance's, since the storage is one for every site: where the local disk keeps
// the files, and the largest request body an upload may send. The rules of uploads are each site's, in MediaSettings.
public sealed class MediaOptions
{
    public const string Section = "FluentCms:Media";

    public LocalStorageOptions Local { get; set; } = new();

    // Bytes. A site's MaxRequestSize above it is cut to it.
    public long MaxRequestBodySize { get; set; } = 256L * 1024 * 1024;
}

public sealed class LocalStorageOptions
{
    // A path relative to the content root, or an absolute one.
    public string Root { get; set; } = "App_Data/media";
}

// The local disk is allowed in Production, and listed so that an operator knows the files are on this machine only.
internal sealed class MediaInsecureDefaults(IFileStorage storage, IHostEnvironment environment) : IInsecureDefaultSource
{
    public const string LocalStorage = "media-local-storage";

    public IEnumerable<InsecureDefault> GetInsecureDefaults()
    {
        if (environment.IsProduction() && storage is LocalFileStorage local)
        {
            yield return new InsecureDefault
            {
                Id = LocalStorage,
                Message = $"Media is stored on the local disk of this instance, in {local.Root}. Every instance needs that directory on one shared volume, and backups must include it; or use a storage provider such as S3 or Azure Blob.",
            };
        }
    }
}
