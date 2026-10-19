using FluentCMS.Core.Extensions;

namespace FluentCMS.Core.Media;

// Where the bytes of media assets live (MED-04). Local disk ships first; any other storage implements this the same way (EXT-01),
// and every implementation must pass MediaStorageConformanceTests in tests/FluentCMS.IntegrationTests/Media. Keys are opaque and
// chosen by the storage; a key it did not hand out is simply not found, never an error and never a path outside the storage.
[ExtensionPoint]
public interface IMediaStorage
{
    // Stored with every asset, so it never changes once released (SchemaNames.IsFieldTypeName).
    string Name { get; }

    Task<StoredMedia> SaveAsync(Stream content, CancellationToken cancellationToken = default);

    // Null for a key the storage does not hold. The caller disposes the stream.
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    // False when there was nothing to delete.
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

// Sha256 is lowercase hex, so an asset can be checked against its bytes.
public sealed record StoredMedia(string Key, long Length, string Sha256);
