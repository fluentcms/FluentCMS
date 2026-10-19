using System.Collections.Concurrent;
using System.Security.Cryptography;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Media;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Media;

// A storage outside Infrastructure registers and is held to the same contract as the local one (EXT-N1).
public sealed class PluginMediaStorageTests : MediaStorageConformanceTests
{
    protected override string StorageName => "sample.memory";

    protected override void AddStorage(IServiceCollection services)
    {
        services.AddFluentCmsExtension<IMediaStorage, MemoryMediaStorage>();
    }
}

// What a plugin storage looks like: using only what Core makes public.
public sealed class MemoryMediaStorage : IMediaStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public string Name => "sample.memory";

    public async Task<StoredMedia> SaveAsync(Stream content, CancellationToken cancellationToken = default)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        var bytes = copy.ToArray();
        var key = Guid.NewGuid().ToString("N");
        _blobs[key] = bytes;

        return new StoredMedia(key, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Stream?>(_blobs.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_blobs.TryRemove(key, out _));
    }
}
