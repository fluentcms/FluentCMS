using FluentCMS.Media;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.StorageServices;

namespace FluentCMS.Plugins.Media.Tests;

// What every IFileStorage does alike: the local disk, S3 on MinIO and Azure Blob on Azurite, with a connection string and with
// a container SAS.
[Collection(StorageServicesGroup.Name)]
public sealed class FileStorageContractTests(StorageServices services) : IDisposable
{
    private readonly DirectoryInfo _local = Directory.CreateTempSubdirectory("fluentcms-storage-");

    public static TheoryData<string> Providers => [LocalDisk, Minio, Azurite, AzuriteSas];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _local.Delete(recursive: true);

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task AFileIsWrittenOpenedFoundDescribedReplacedAndDeleted(string provider)
    {
        var storage = await services.StorageAsync(provider, _local);
        var key = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}/original.png";
        var before = DateTimeOffset.UtcNow.AddMinutes(-10);

        await storage.WriteAsync(key, new MemoryStream([1, 2, 3]), Token);

        (await ReadAsync(storage, key)).ShouldBe([1, 2, 3]);
        (await storage.ExistsAsync(key, Token)).ShouldBeTrue();
        var metadata = (await storage.GetMetadataAsync(key, Token)).ShouldNotBeNull();
        metadata.Size.ShouldBe(3);
        metadata.LastModified.ShouldBeGreaterThan(before);
        metadata.LastModified.Offset.ShouldBe(TimeSpan.Zero);

        await storage.WriteAsync(key, new MemoryStream([4, 5]), Token);

        (await ReadAsync(storage, key)).ShouldBe([4, 5]);
        (await storage.GetMetadataAsync(key, Token)).ShouldNotBeNull().Size.ShouldBe(2);
        (await storage.DeleteAsync(key, Token)).ShouldBeTrue();
        (await storage.ExistsAsync(key, Token)).ShouldBeFalse();
        (await storage.OpenReadAsync(key, Token)).ShouldBeNull();
        (await storage.DeleteAsync(key, Token)).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task AMissingKeyIsNoFile(string provider)
    {
        var storage = await services.StorageAsync(provider, _local);
        var key = $"{Guid.NewGuid():N}/missing.jpg";

        (await storage.OpenReadAsync(key, Token)).ShouldBeNull();
        (await storage.ExistsAsync(key, Token)).ShouldBeFalse();
        (await storage.GetMetadataAsync(key, Token)).ShouldBeNull();
        (await storage.DeleteAsync(key, Token)).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task AWriteReadsTheContentFromWhereItStandsToItsEndEvenWhenItCannotSeek(string provider)
    {
        var storage = await services.StorageAsync(provider, _local);
        var seekable = new MemoryStream([9, 8, 7, 6]) { Position = 1 };

        await storage.WriteAsync("from/position.bin", seekable, Token);
        await storage.WriteAsync("from/forward-only.bin", new ForwardOnlyStream([5, 4, 3]), Token);

        (await ReadAsync(storage, "from/position.bin")).ShouldBe([8, 7, 6]);
        (await ReadAsync(storage, "from/forward-only.bin")).ShouldBe([5, 4, 3]);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task AKeyThatIsNotPlainSegmentsIsRefusedBeforeTheStorageIsAsked(string provider)
    {
        var storage = await services.StorageAsync(provider, _local);

        foreach (var key in new[] { "../outside.png", "a//b", "..", "a/b c", "", "a/./b" })
        {
            await Should.ThrowAsync<ArgumentException>(() => storage.ExistsAsync(key, Token));
            await Should.ThrowAsync<ArgumentException>(() => storage.OpenReadAsync(key, Token));
            await Should.ThrowAsync<ArgumentException>(() => storage.WriteAsync(key, new MemoryStream([1]), Token));
            await Should.ThrowAsync<ArgumentException>(() => storage.DeleteAsync(key, Token));
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task TheStorageIsReadyWhileItAnswers(string provider)
    {
        var storage = await services.StorageAsync(provider, _local);

        var result = await storage.ShouldBeAssignableTo<IHealthCheck>()!.CheckHealthAsync(new HealthCheckContext(), Token);

        result.Status.ShouldBe(HealthStatus.Healthy, result.Description);
        result.Exception.ShouldBeNull();
    }

    private static async Task<byte[]> ReadAsync(IFileStorage storage, string key)
    {
        await using var stream = (await storage.OpenReadAsync(key, Token)).ShouldNotBeNull();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Token);
        return copy.ToArray();
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
