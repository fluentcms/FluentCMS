using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FluentCMS.Plugins.Media.AzureBlob;

// A blob read with ranged downloads of the version its properties found.
internal sealed class AzureBlobStream(BlobClient blob, BlobProperties properties) : StorageReadStream(properties.ContentLength)
{
    protected override async Task<Stream> OpenBodyAsync(long position, CancellationToken cancellationToken)
    {
        var options = new BlobDownloadOptions { Range = new HttpRange(position), Conditions = new BlobRequestConditions { IfMatch = properties.ETag } };
        var response = await blob.DownloadStreamingAsync(options, cancellationToken);
        return response.Value.Content;
    }
}
