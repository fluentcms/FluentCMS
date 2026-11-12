using FluentCMS.Media;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

// The default file storage: a directory on the local disk, FluentCms:Media:Local:Root, under the content root unless it is
// absolute. A key is checked before it becomes a path, so no key leaves the root. A write goes to a temporary file beside
// the target and is moved over it, so that a reader sees the old file or the new one. Ready while a file can be created in the
// root, so a volume that is missing or read-only takes the instance out of rotation.
internal sealed class LocalFileStorage(IOptions<MediaOptions> options, IHostEnvironment environment) : IFileStorage, IHealthCheck
{
    public const string ProviderName = "local";

    // The key of the DirectoryInfo of the root among the services.
    public const string RootKey = "FluentCMS.Media.LocalRoot";

    public string Name => ProviderName;

    public string Root { get; } = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.Local.Root));

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        try
        {
            return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, cancellationToken);
                await file.FlushAsync(cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(File.Exists(PathOf(key)));

    public Task<FileStorageMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(PathOf(key));
        return Task.FromResult(file.Exists ? new FileStorageMetadata(file.Length, file.LastWriteTimeUtc) : null);
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(Root);
            using (new FileStream(Path.Combine(Root, $".ready-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return Task.FromResult(HealthCheckResult.Healthy("The media directory can be written."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"The media directory {Root} cannot be written: {exception.Message}"));
        }
    }

    private string PathOf(string key)
    {
        FileStorageKeys.Checked(key);

        var path = Path.GetFullPath(Path.Combine(Root, key));
        var prefix = Path.EndsInDirectorySeparator(Root) ? Root : Root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException($"'{key}' leaves the storage root.", nameof(key));
    }
}
