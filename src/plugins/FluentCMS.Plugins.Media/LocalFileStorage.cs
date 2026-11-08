using System.Text.RegularExpressions;
using FluentCMS.Media;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

// The default file storage: a directory on the local disk, FluentCms:Media:Local:Root, under the content root unless it is
// absolute. A key is checked before it becomes a path, so no key leaves the root. A write goes to a temporary file beside
// the target and is moved over it, so that a reader sees the old file or the new one.
internal sealed partial class LocalFileStorage(IOptions<MediaOptions> options, IHostEnvironment environment) : IFileStorage
{
    public const string ProviderName = "local";

    private const int KeyMaxLength = 512;

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

    public static bool IsValidKey(string? key) =>
        key is { Length: > 0 and <= KeyMaxLength } && key.Split('/').All(segment => Segment().IsMatch(segment) && segment.Any(character => character != '.'));

    private string PathOf(string key)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException($"'{key}' is not a storage key: segments of letters, digits, dots, hyphens and underscores joined by slashes.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(Root, key));
        var prefix = Path.EndsInDirectorySeparator(Root) ? Root : Root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException($"'{key}' leaves the storage root.", nameof(key));
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Segment();
}
