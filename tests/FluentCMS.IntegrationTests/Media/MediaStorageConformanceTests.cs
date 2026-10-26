using System.Security.Cryptography;
using FluentCMS.Core;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Media;
using FluentCMS.Core.Schema;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Media;

// The contract every media storage meets, built in or not (MED-04, EXT-N1). Derive one class per storage: name it, and register it in
// AddStorage the way the application would.
[ConformanceFor(typeof(IMediaStorage))]
public abstract class MediaStorageConformanceTests : IDisposable
{
    private ServiceProvider? _provider;

    protected abstract string StorageName { get; }

    protected IMediaStorage Storage => Provider.GetRequiredService<MediaStorageRegistry>().Find(StorageName)
        ?? throw new InvalidOperationException($"No media storage is named '{StorageName}'.");

    private ServiceProvider Provider => _provider ??= BuildProvider();

    protected abstract void AddStorage(IServiceCollection services);

    [Fact]
    public void Is_registered_through_the_extension_point_once_under_a_valid_name()
    {
        Assert.True(SchemaNames.IsFieldTypeName(StorageName), $"'{StorageName}' is not a valid media storage name.");
        Assert.Equal(StorageName, Storage.Name);
        Assert.Single(Provider.GetRequiredService<IExtensionCatalog>().For<IMediaStorage>(), descriptor => descriptor.Implementation == Storage.GetType());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(1_000_003)]
    public async Task Bytes_read_back_as_written_with_their_length_and_hash(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);

        var stored = await SaveAsync(bytes);

        Assert.Equal(length, stored.Length);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), stored.Sha256);
        Assert.Equal(bytes, await ReadAsync(stored.Key));
    }

    [Fact]
    public async Task Every_save_gets_a_key_of_its_own()
    {
        var first = await SaveAsync([1, 2, 3]);
        var second = await SaveAsync([1, 2, 3]);

        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(first.Sha256, second.Sha256);
    }

    [Fact]
    public async Task A_deleted_key_reads_as_missing()
    {
        var stored = await SaveAsync([4, 5, 6]);

        Assert.True(await Storage.DeleteAsync(stored.Key));
        Assert.Null(await Storage.OpenReadAsync(stored.Key));
        Assert.False(await Storage.DeleteAsync(stored.Key));
    }

    // A key comes from stored metadata, which an agent can never write, but a storage still treats a strange one as not found.
    [Theory]
    [InlineData("")]
    [InlineData("../secret")]
    [InlineData("..\\secret")]
    [InlineData("a/b")]
    [InlineData("/etc/passwd")]
    [InlineData("0199a000000070008000000000000001")]
    public async Task A_key_it_did_not_hand_out_is_not_found(string key)
    {
        Assert.Null(await Storage.OpenReadAsync(key));
        Assert.False(await Storage.DeleteAsync(key));
    }

    public void Dispose()
    {
        _provider?.Dispose();
        Cleanup();
        GC.SuppressFinalize(this);
    }

    protected virtual void Cleanup()
    {
    }

    protected async Task<StoredMedia> SaveAsync(byte[] bytes)
    {
        using var content = new MemoryStream(bytes);
        return await Storage.SaveAsync(content);
    }

    protected async Task<byte[]> ReadAsync(string key)
    {
        await using var stream = await Storage.OpenReadAsync(key) ?? throw new InvalidOperationException($"Key {key} is not stored.");
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection().AddFluentCmsCore();
        AddStorage(services);
        return services.BuildServiceProvider();
    }
}
