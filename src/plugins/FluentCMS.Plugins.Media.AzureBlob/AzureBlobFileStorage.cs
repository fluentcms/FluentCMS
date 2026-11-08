using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentCMS.Media;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media.AzureBlob;

// Files as the blobs of one container, named by the key prefix and the key. The container stays private, and bytes leave
// through /media and signed URLs only. A SAS needs read, write, delete and list; list is the ready check's, the first blob
// under the prefix, which works with a container SAS where the container's own properties do not.
internal sealed class AzureBlobFileStorage(IOptions<AzureBlobStorageOptions> options) : IFileStorage, IHealthCheck
{
    public const string ProviderName = "azure-blob";

    private readonly BlobContainerClient _container = options.Value.CreateClient();
    private readonly string _prefix = options.Value.Prefix;

    public string Name => ProviderName;

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var blob = Blob(key);
        try
        {
            var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException exception) when (IsMissingBlob(exception))
        {
            return null;
        }
    }

    // One upload, committed whole, so a reader sees the old blob or the new one.
    public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        await Blob(key).UploadAsync(content, overwrite: true, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var blob = Blob(key);
        try
        {
            await blob.DeleteAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch (RequestFailedException exception) when (IsMissingBlob(exception))
        {
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        await GetMetadataAsync(key, cancellationToken) is not null;

    public async Task<FileStorageMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        var blob = Blob(key);
        try
        {
            BlobProperties properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            return new FileStorageMetadata(properties.ContentLength, properties.LastModified);
        }
        catch (RequestFailedException exception) when (IsMissingBlob(exception))
        {
            return null;
        }
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var _ in _container.GetBlobsAsync(new GetBlobsOptions { Prefix = _prefix }, cancellationToken).AsPages(pageSizeHint: 1))
            {
                break;
            }

            return HealthCheckResult.Healthy($"The Azure Blob container '{_container.Name}' answers.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy($"The Azure Blob container '{_container.Name}' of the account '{_container.AccountName}' cannot be reached: {Reason(exception)}.");
        }
    }

    // What failed by its status, its code or its type, never by its message, which quotes the request.
    internal static string Reason(Exception exception)
    {
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            switch (inner)
            {
                case RequestFailedException { Status: not 0 } failed:
                    return $"{failed.Status} {failed.ErrorCode}".TrimEnd();
                case HttpRequestException http:
                    return $"{nameof(HttpRequestException)} {http.HttpRequestError}";
            }
        }

        return exception.GetType().Name;
    }

    // A 404 of a missing container is a storage out of order, not a file that is not there.
    private static bool IsMissingBlob(RequestFailedException exception) =>
        exception.Status == 404 && exception.ErrorCode != BlobErrorCode.ContainerNotFound;

    private BlobClient Blob(string key) => _container.GetBlobClient(_prefix + FileStorageKeys.Checked(key));
}
