namespace FluentCMS.Media;

/// <summary>
/// Where the media plugin keeps the bytes of its assets: a provider slot whose default is the local disk, replaced by a
/// storage plugin or the host. Each asset records the <see cref="Name"/> of the storage it was written with and its key, so
/// that a later change of storage leaves older assets where they are. A key is a relative path of segments of letters,
/// digits, dots, hyphens and underscores joined by slashes, such as <c>0f8f.../original.jpg</c>; a key with any other
/// character, an empty segment or a segment of dots alone is refused with an <see cref="ArgumentException"/>.
/// </summary>
public interface IFileStorage
{
    /// <summary>The name an asset records, such as <c>local</c>. It must not change once assets were written.</summary>
    string Name { get; }

    /// <summary>Opens a file for reading.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The stream, which the caller disposes, or <see langword="null"/> when there is no such file.</returns>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Writes a file, replacing one with the same key. A reader sees the old file or the new one, never a part.</summary>
    /// <param name="key">The key.</param>
    /// <param name="content">The bytes, read to their end.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the file is stored.</returns>
    Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Deletes a file.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see langword="true"/> when there was a file to delete.</returns>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Whether a file exists.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see langword="true"/> when it exists.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Reads what the storage knows of a file.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The metadata, or <see langword="null"/> when there is no such file.</returns>
    Task<FileStorageMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>What a storage knows of a file.</summary>
/// <param name="Size">Its length in bytes.</param>
/// <param name="LastModified">When it was last written.</param>
public sealed record FileStorageMetadata(long Size, DateTimeOffset LastModified);
