using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentCMS.Core.Media;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Media;

// Bytes on the local disk under Media__LocalDisk__Root (MED-04, ADR-0004). A key is a random id, fanned out over two directory levels
// so no directory grows too large; anything else is not a key, so no caller can reach a path outside the root.
internal sealed partial class LocalDiskMediaStorage(IOptions<LocalDiskOptions> options) : IMediaStorage
{
    public const string StorageName = "local";

    private const int BufferSize = 81920;

    public string Name => StorageName;

    // A file is written under a temporary name and renamed into place, so a reader never sees half of it.
    public async Task<StoredMedia> SaveAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var key = Guid.CreateVersion7().ToString("N");
        var path = PathOf(key)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = 0;

            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    length += read;
                }
            }

            File.Move(temporary, path);
            return new StoredMedia(key, length, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (PathOf(key) is not { } path)
        {
            return Task.FromResult<Stream?>(null);
        }

        try
        {
            return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (PathOf(key) is not { } path || !File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    private string? PathOf(string? key)
    {
        if (key is null || !KeyPattern().IsMatch(key))
        {
            return null;
        }

        return Path.Combine(Path.GetFullPath(options.Value.Root), key[..2], key[2..4], key);
    }

    [GeneratedRegex("^[0-9a-f]{32}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
