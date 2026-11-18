using Amazon.S3;
using Amazon.S3.Model;

namespace FluentCMS.Plugins.Media.S3;

// An object read with ranged GETs of the version its HEAD found.
internal sealed class S3ObjectStream(IAmazonS3 client, string bucket, string key, GetObjectMetadataResponse head) : StorageReadStream(head.ContentLength)
{
    protected override async Task<Stream> OpenBodyAsync(long position, CancellationToken cancellationToken)
    {
        var response = await client.GetObjectAsync(
            new GetObjectRequest { BucketName = bucket, Key = key, ByteRange = new ByteRange(position, Length - 1), EtagToMatch = head.ETag },
            cancellationToken);
        return response.ResponseStream;
    }
}
