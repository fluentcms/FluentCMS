using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FluentCMS.Media;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media.S3;

// Files as the objects of one bucket, named by the key prefix and the key. Objects are written with no ACL, so they are as
// private as the bucket, and bytes leave through /media and signed URLs only. Telling a missing object from a refused one, and
// the ready check, a HEAD of the bucket, both need s3:ListBucket beside s3:GetObject, s3:PutObject and s3:DeleteObject.
internal sealed class S3FileStorage(IOptions<S3StorageOptions> options) : IFileStorage, IHealthCheck, IDisposable
{
    public const string ProviderName = "s3";

    private readonly AmazonS3Client _client = options.Value.CreateClient();
    private readonly string _bucket = options.Value.Bucket;
    private readonly string _prefix = options.Value.Prefix;

    public string Name => ProviderName;

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var name = ObjectKey(key);
        try
        {
            var response = await _client.GetObjectAsync(_bucket, name, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound && exception.ErrorCode != "NoSuchBucket")
        {
            return null;
        }
    }

    // One PUT, so a reader sees the old object or the new one. The SDK needs the length first, so a stream that cannot seek is
    // copied to a temporary file.
    public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var name = ObjectKey(key);
        var buffered = content.CanSeek ? null : await BufferAsync(content, cancellationToken);
        try
        {
            await _client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = name,
                    InputStream = buffered ?? content,
                    AutoCloseStream = false,
                    AutoResetStreamPosition = false,
                },
                cancellationToken);
        }
        finally
        {
            if (buffered is not null)
            {
                await buffered.DisposeAsync();
            }
        }
    }

    // S3 answers a DELETE alike whether the object was there, so it is looked up first.
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var name = ObjectKey(key);
        if (await HeadAsync(name, cancellationToken) is null)
        {
            return false;
        }

        await _client.DeleteObjectAsync(_bucket, name, cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        await HeadAsync(ObjectKey(key), cancellationToken) is not null;

    public async Task<FileStorageMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default) =>
        await HeadAsync(ObjectKey(key), cancellationToken) is { } head
            ? new FileStorageMetadata(head.ContentLength, Utc(head.LastModified))
            : null;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.HeadBucketAsync(new HeadBucketRequest { BucketName = _bucket }, cancellationToken);
            return HealthCheckResult.Healthy($"The S3 bucket '{_bucket}' answers.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy($"The S3 bucket '{_bucket}' cannot be reached: {Reason(exception)}.");
        }
    }

    public void Dispose() => _client.Dispose();

    // What failed by its status, its code or its type, never by its message, which may quote a request.
    internal static string Reason(Exception exception)
    {
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            switch (inner)
            {
                case AmazonServiceException { StatusCode: not 0 } service:
                    return $"{(int)service.StatusCode} {service.ErrorCode}".TrimEnd();
                case HttpRequestException http:
                    return $"{nameof(HttpRequestException)} {http.HttpRequestError}";
            }
        }

        return exception.GetType().Name;
    }

    private async Task<GetObjectMetadataResponse?> HeadAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetObjectMetadataAsync(_bucket, name, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private string ObjectKey(string key) => _prefix + FileStorageKeys.Checked(key);

    private static DateTimeOffset Utc(DateTime? at) => at switch
    {
        { Kind: DateTimeKind.Unspecified } value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
        { } value => new DateTimeOffset(value.ToUniversalTime()),
        null => DateTimeOffset.MinValue,
    };

    private static async Task<Stream> BufferAsync(Stream content, CancellationToken cancellationToken)
    {
        var file = new FileStream(Path.Combine(Path.GetTempPath(), $"fluentcms-s3-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            await content.CopyToAsync(file, cancellationToken);
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }
}
