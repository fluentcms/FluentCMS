using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Amazon.S3.Model;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentCMS.Core.Jobs;
using FluentCMS.Jobs;
using FluentCMS.Media;
using FluentCMS.Plugins.Media.AzureBlob;
using FluentCMS.Plugins.Media.S3;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.StorageServices;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

// The media plugin on S3 (MinIO) and Azure Blob (Azurite): uploads, transforms and their variants in the bucket or container,
// assets of the local disk served after the storage moved, and the ready check.
[Collection(StorageServicesGroup.Name)]
public sealed class StorageProviderTests(StorageServices services) : IDisposable
{
    private const string Health = "/api/management/v1/system/health";

    private readonly DirectoryInfo _local = Directory.CreateTempSubdirectory("fluentcms-storage-");

    public static TheoryData<string> CloudProviders => [Minio, Azurite, AzuriteSas];

    public void Dispose() => _local.Delete(recursive: true);

    [Theory]
    [MemberData(nameof(CloudProviders))]
    public async Task AnImageUploadedToTheStorageTransformsThroughASignedUrlAndItsVariantIsStoredBesideIt(string provider)
    {
        var cloud = await CloudAsync(provider);
        await using var host = StorageHosts.Create(_local, StorageHosts.SigningKey(), cloud.Use);
        using var admin = await StorageHosts.AdminAsync(host);
        using var anonymous = host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));
        var url = await ImageUrlAsync(host, asset, new MediaImageOptions { Width = 100, Format = MediaImageFormat.Webp });

        using (var response = await FetchAsync(anonymous, url))
        {
            var (type, bytes) = await ImageAsync(response);
            type.ShouldBe("image/webp");
            using var image = Decode(bytes);
            (image.Width, image.Height).ShouldBe((100, 50));
        }

        var (original, variant) = await KeysAsync(host, asset.Id);
        original.Provider.ShouldBe(cloud.Name);
        variant.ShouldStartWith(original.Key[..(original.Key.LastIndexOf('/') + 1)]);
        (await cloud.ListAsync(original.Key[..original.Key.LastIndexOf('/')])).ShouldBe([original.Key, variant], ignoreOrder: true);
        Directory.EnumerateFiles(_local.FullName, "*", SearchOption.AllDirectories).ShouldBeEmpty();

        // The second request is served from the variant in the storage.
        using (var again = await FetchAsync(anonymous, url))
        {
            (await ImageAsync(again)).Type.ShouldBe("image/webp");
        }
    }

    [Theory]
    [MemberData(nameof(CloudProviders))]
    public async Task AnAssetWrittenToTheLocalDiskIsStillServedAfterTheStorageMoves(string provider)
    {
        var database = Path.Combine(_local.FullName, "cms.db");
        var media = _local.CreateSubdirectory("media");
        var key = StorageHosts.SigningKey();
        MediaAsset before;
        await using (var local = StorageHosts.Create(media, key, database: database))
        {
            using var admin = await StorageHosts.AdminAsync(local);
            before = await UploadAsync(admin, Bands(400, 200));
        }

        var cloud = await CloudAsync(provider);
        await using var host = StorageHosts.Create(media, key, cloud.Use, database);
        using var anonymous = host.CreateClient();
        using (var administrator = await StorageHosts.AdminAsync(host))
        {
            var after = await UploadAsync(administrator, Bands(40, 20));
            (await KeysAsync(host, after.Id, variants: false)).Original.Provider.ShouldBe(cloud.Name);
        }

        using (var response = await FetchAsync(anonymous, await ImageUrlAsync(host, before, new MediaImageOptions { Width = 100, Format = MediaImageFormat.Png })))
        {
            using var image = Decode((await ImageAsync(response)).Bytes);
            (image.Width, image.Height).ShouldBe((100, 50));
        }

        // The variant goes where its asset is: beside it on the local disk, not in the new storage.
        var (original, variant) = await KeysAsync(host, before.Id);
        original.Provider.ShouldBe(LocalFileStorage.ProviderName);
        File.Exists(Path.Combine(media.FullName, variant)).ShouldBeTrue();
        (await cloud.ListAsync(original.Key[..original.Key.LastIndexOf('/')])).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(CloudProviders))]
    public async Task AnAssetWrittenToTheLocalDiskIsStillDownloadedReplacedAndPurgedAfterTheStorageMoves(string provider)
    {
        var database = Path.Combine(_local.FullName, "cms.db");
        var media = _local.CreateSubdirectory("media");
        var key = StorageHosts.SigningKey();
        var pdf = Files.Pdf();
        MediaAsset document;
        MediaAsset photo;
        await using (var local = StorageHosts.Create(media, key, database: database))
        {
            using var admin = await StorageHosts.AdminAsync(local);
            using var client = local.CreateClient();
            document = await UploadAsync(admin, pdf, "plan.pdf");
            photo = await UploadAsync(admin, Bands(40, 20));
            using var variant = await FetchAsync(client, await ImageUrlAsync(local, photo, new MediaImageOptions { Width = 10 }));
            variant.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        Directory.EnumerateFiles(media.FullName, "*", SearchOption.AllDirectories).Count().ShouldBe(3);
        var cloud = await CloudAsync(provider);
        await using var host = StorageHosts.Create(media, key, cloud.Use, database);
        using var administrator = await StorageHosts.AdminAsync(host);
        using var anonymous = host.CreateClient();

        (await DownloadAsync(host, administrator, anonymous, document.Id)).ShouldBe(pdf);

        // The new file goes to the new storage; the old one and its variant leave the disk.
        var replacement = Bands(20, 10);
        using (var replaced = await ReplaceAsync(administrator, photo.Id, ("small.png", replacement)))
        {
            replaced.StatusCode.ShouldBe(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync(Token));
        }

        var (now, _) = await KeysAsync(host, photo.Id, variants: false);
        now.Provider.ShouldBe(cloud.Name);
        (await cloud.ListAsync(now.Key[..now.Key.LastIndexOf('/')])).ShouldBe([now.Key]);
        Directory.Exists(Path.Combine(media.FullName, photo.SiteId.ToString("N"), photo.Id.ToString("N"))).ShouldBeTrue();
        Directory.EnumerateFiles(Path.Combine(media.FullName, photo.SiteId.ToString("N"), photo.Id.ToString("N"))).ShouldBeEmpty();
        (await DownloadAsync(host, administrator, anonymous, photo.Id)).ShouldBe(replacement);

        // Purged from the disk once deleted longer ago than the retention.
        (await administrator.DeleteAsync(new Uri($"{Root}/{document.Id:D}", UriKind.Relative), Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        host.Time.Advance(TimeSpan.FromDays(31));
        await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaVariantCleanupJob, MediaVariantCleanupPayload>(new MediaVariantCleanupPayload(), cancellationToken: Token);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
            while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
            {
            }
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking().AnyAsync(asset => asset.Id == document.Id, Token)).ShouldBeFalse();
        }

        Directory.EnumerateFiles(media.FullName, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(CloudProviders))]
    public async Task ReadyIsUnhealthyWhileTheBucketOrContainerCannotBeReached(string provider)
    {
        var cloud = await CloudAsync(provider);
        await using (var reachable = StorageHosts.Create(_local, StorageHosts.SigningKey(), cloud.Use))
        {
            using var client = reachable.CreateClient();
            using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);
            ready.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await StorageCheckAsync(reachable))["status"]!.GetValue<string>().ShouldBe("Healthy");
        }

        await cloud.RemoveAsync();
        await using var host = StorageHosts.Create(_local, StorageHosts.SigningKey(), cloud.Use);
        using var anonymous = host.CreateClient();
        using var unready = await anonymous.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);
        unready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await unready.Content.ReadAsStringAsync(Token)).ShouldBe("Unhealthy");

        var check = await StorageCheckAsync(host);
        check["status"]!.GetValue<string>().ShouldBe("Unhealthy");
        check["description"]!.GetValue<string>().ShouldContain("404");
        check["error"].ShouldBeNull();
    }

    [Fact]
    public async Task TheLocalDiskIsReadyWhileItsDirectoryCanBeWritten()
    {
        await using var host = StorageHosts.Create(_local, StorageHosts.SigningKey());
        var check = await StorageCheckAsync(host);

        check["status"]!.GetValue<string>().ShouldBe("Healthy");
        check["tags"]!.AsArray().Select(tag => tag!.GetValue<string>()).ShouldBe(["ready"]);
        Directory.EnumerateFileSystemEntries(_local.FullName).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheLocalDiskIsNotReadyWhenItsRootCannotBeADirectory()
    {
        var file = Path.Combine(_local.FullName, "not-a-directory");
        await File.WriteAllBytesAsync(file, [1], Token);
        await using var host = StorageHosts.Create(new DirectoryInfo(file), StorageHosts.SigningKey());
        using var client = host.CreateClient();

        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await StorageCheckAsync(host))["status"]!.GetValue<string>().ShouldBe("Unhealthy");
    }

    internal static async Task<string> ImageUrlAsync(FluentCmsTestHost host, MediaAsset asset, MediaImageOptions options)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediaService>().GetImageUrlAsync(asset, options, Token);
    }

    internal static async Task<JsonNode> StorageCheckAsync(FluentCmsTestHost host)
    {
        using var admin = await StorageHosts.AdminAsync(host);
        var report = await GetAsync(admin, Health);
        return report["checks"]!.AsArray().Single(check => check!["name"]!.GetValue<string>() == MediaStorageHealthCheck.Name)!;
    }

    private static async Task<byte[]> DownloadAsync(FluentCmsTestHost host, HttpClient admin, HttpClient anonymous, Guid id)
    {
        using var issued = await admin.PostAsync(new Uri($"{Root}/{id:D}/download-url", UriKind.Relative), null, Token);
        using var served = await FetchAsync(anonymous, (await JsonAsync(issued))["url"]!.GetValue<string>());
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await served.Content.ReadAsByteArrayAsync(Token);
    }

    private static async Task<HttpResponseMessage> ReplaceAsync(HttpClient client, Guid id, (string Name, byte[] Bytes) file)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file.Bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", file.Name);
        return await client.PostAsync(new Uri($"{Root}/{id:D}/replace", UriKind.Relative), form, Token);
    }

    private static async Task<((string Provider, string Key) Original, string Variant)> KeysAsync(FluentCmsTestHost host, Guid id, bool variants = true)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var asset = await database.Set<Asset>().AsNoTracking().SingleAsync(item => item.Id == id, Token);
        var variant = variants ? (await database.Set<Variant>().AsNoTracking().SingleAsync(item => item.AssetId == id, Token)).StorageKey : string.Empty;
        return ((asset.StorageProvider, asset.StorageKey), variant);
    }

    private async Task<Cloud> CloudAsync(string provider)
    {
        switch (provider)
        {
            case Minio:
                var bucket = await services.BucketAsync();
                return new Cloud(
                    S3FileStorage.ProviderName,
                    media => media.UseS3(Section(bucket)),
                    async prefix =>
                    {
                        using var client = bucket.CreateClient();
                        var listed = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket.Bucket, Prefix = $"{bucket.Prefix}{prefix}/" }, Token);
                        return [.. (listed.S3Objects ?? []).Select(item => item.Key[bucket.Prefix.Length..])];
                    },
                    async () =>
                    {
                        using var client = bucket.CreateClient();
                        await client.DeleteBucketAsync(bucket.Bucket, Token);
                    });
            default:
                var container = await services.ContainerAsync(sas: provider == AzuriteSas);
                var client = container.CreateClient();
                return new Cloud(
                    AzureBlobFileStorage.ProviderName,
                    media => media.UseAzureBlob(Section(container)),
                    async prefix =>
                    {
                        List<string> names = [];
                        await foreach (var blob in client.GetBlobsAsync(new GetBlobsOptions { Prefix = $"{container.Prefix}{prefix}/" }, Token))
                        {
                            names.Add(blob.Name[container.Prefix.Length..]);
                        }

                        return names;
                    },
                    async () => await Owner(container).DeleteAsync(cancellationToken: Token));
        }
    }

    // A SAS cannot delete its own container, so the account removes it.
    private BlobContainerClient Owner(AzureBlobStorageOptions container) =>
        container.ConnectionString is not null ? container.CreateClient() : services.Account().GetBlobContainerClient(container.CreateClient().Name);

    private sealed record Cloud(string Name, Action<MediaPlugin> Use, Func<string, Task<List<string>>> ListAsync, Func<Task> RemoveAsync);
}
