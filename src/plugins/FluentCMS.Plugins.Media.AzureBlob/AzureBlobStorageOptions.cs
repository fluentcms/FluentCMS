using System.Text.RegularExpressions;
using Azure.Storage.Blobs;

namespace FluentCMS.Plugins.Media.AzureBlob;

// Bound from the section given to UseAzureBlob: a connection string with the name of a container, or the URL of a container
// with a SAS token in its query. Neither is ever logged or reported. Managed identity would need Azure.Identity, and is not here.
public sealed partial class AzureBlobStorageOptions
{
    public string? ConnectionString { get; set; }

    // The container, with ConnectionString.
    public string? Container { get; set; }

    // https://{account}.blob.core.windows.net/{container}?{sas}, instead of ConnectionString and Container.
    public Uri? ContainerUrl { get; set; }

    // Put before every key, such as media/; segments of a key, joined by slashes.
    public string? KeyPrefix { get; set; }

    internal string Prefix => KeyPrefix?.Trim('/') is { Length: > 0 } prefix ? prefix + "/" : string.Empty;

    internal bool IsValid() =>
        (string.IsNullOrEmpty(KeyPrefix?.Trim('/')) || FileStorageKeys.IsValid(KeyPrefix.Trim('/')))
        && (string.IsNullOrEmpty(ConnectionString)
            ? ContainerUrl is { IsAbsoluteUri: true, Scheme: "http" or "https" } url && string.IsNullOrEmpty(Container) && url.Query.Contains("sig=", StringComparison.Ordinal)
            : ContainerUrl is null && ContainerName().IsMatch(Container ?? string.Empty) && ParsesAsConnectionString(ConnectionString));

    internal BlobContainerClient CreateClient() =>
        string.IsNullOrEmpty(ConnectionString) ? new BlobContainerClient(ContainerUrl) : new BlobContainerClient(ConnectionString, Container);

    // Parsed here, so that a malformed string stops the host with this class's message rather than the parser's.
    private static bool ParsesAsConnectionString(string connectionString)
    {
        try
        {
            _ = new BlobServiceClient(connectionString);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^(?=.{3,63}\z)[a-z0-9](?:[a-z0-9]|-(?!-))*[a-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex ContainerName();
}
