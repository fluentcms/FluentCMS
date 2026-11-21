using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using FluentCMS.Media;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

// The width and height of an image are those it is shown at, after its EXIF orientation, as the processor reads them, and
// MediaDimensionsJob gives them to the images stored before.
public sealed class MediaDimensionsTests(MediaHost media) : IClassFixture<MediaHost>
{
    [Fact]
    public async Task ATurnedPhotoHasTheSizeItIsShownAtAfterAnUploadAndAReplace()
    {
        using var admin = await media.AdminAsync();

        var turned = await UploadAsync(admin, TurnedJpeg(400, 100), "turned.jpg");
        var plain = await UploadAsync(admin, Bands(40, 20, SkiaSharp.SKEncodedImageFormat.Jpeg), "plain.jpg");

        (turned.Width, turned.Height).ShouldBe((100, 400));
        (plain.Width, plain.Height).ShouldBe((40, 20));

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TurnedJpeg(300, 60));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "again.jpg");
        using var replaced = await admin.PostAsync(new Uri($"{Root}/{plain.Id:D}/replace", UriKind.Relative), form, Token);
        var asset = (await JsonAsync(replaced)).Deserialize<MediaAsset>(JsonSerializerOptions.Web)!;
        (asset.Width, asset.Height).ShouldBe((60, 300));
    }

    // Three JPEGs stored with the size of their header, one of them with its type in capitals as a database may hold it, and
    // one stored right. A run reads one image and enqueues the next until a run finds none and marks the check done; nothing
    // but the size changes, and a run after that reads nothing.
    [Fact]
    public async Task TheBackfillGivesImagesStoredBeforeTheSizeTheyAreShownAtAndNothingElse()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        await media.Services.GetServices<IHostedService>().OfType<MediaStartup>().Single().ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        await RunJobsAsync();
        var first = await UploadAsync(admin, TurnedJpeg(400, 100), "first.jpg");
        var second = await UploadAsync(admin, TurnedJpeg(200, 50), "second.jpg");
        var capitals = await UploadAsync(admin, TurnedJpeg(300, 60), "capitals.jpg");
        var plain = await UploadAsync(admin, Bands(40, 20, SkiaSharp.SKEncodedImageFormat.Jpeg), "plain.jpg");
        await StoreAsync(first.Id, 400, 100);
        await StoreAsync(second.Id, 200, 50);
        await StoreAsync(capitals.Id, 300, 60, "IMAGE/JPEG");
        var before = await RowsAsync(first.Id, second.Id, capitals.Id, plain.Id);
        (await DeliveredAsync(anonymous, first.Id)).ShouldBe((400, 100));
        await StateAsync("");

        await media.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaDimensionsJob, MediaDimensionsPayload>(new MediaDimensionsPayload(BatchSize: 1), cancellationToken: Token);
        (await RunJobsAsync()).ShouldBeGreaterThanOrEqualTo(5);

        var after = await RowsAsync(first.Id, second.Id, capitals.Id, plain.Id);
        after.Select(asset => (asset.Width ?? 0, asset.Height ?? 0)).ShouldBe([(100, 400), (50, 200), (60, 300), (40, 20)]);
        after.Select(asset => (asset.Version, asset.UpdatedAt, asset.UpdatedBy)).ShouldBe(before.Select(asset => (asset.Version, asset.UpdatedAt, asset.UpdatedBy)));
        (await StateAsync()).ShouldBe(MediaDimensionsJob.Done);
        (await DeliveredAsync(anonymous, first.Id)).ShouldBe((100, 400));
        await media.DrainOutboxAsync();
        var actions = (await GetAsync(admin, $"/api/management/v1/activity?targetType=media&targetId={first.Id:D}"))["items"]!.AsArray().Select(row => row!["action"]!.GetValue<string>());
        actions.ShouldBe(["media.uploaded"]);

        await StoreAsync(second.Id, 200, 50);
        await media.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaDimensionsJob, MediaDimensionsPayload>(new MediaDimensionsPayload(), cancellationToken: Token);
        await RunJobsAsync();

        (await RowsAsync(second.Id)).Single().Width.ShouldBe(200);
    }

    // A start enqueues a chain only when none claimed the check within the lease, as one that failed for good, and never
    // once the library was gone over. Only the runs of the check are counted, since moving the clock makes core's own jobs due.
    [Fact]
    public async Task AStartEnqueuesOneChainAndNoneOnceTheCheckIsDone()
    {
        await StartedAsync();
        await using var scope = media.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var scheduler = media.Services.GetRequiredService<IJobScheduler>();
        await database.Set<SigningKey>().Where(row => row.Id == MediaDimensionsJob.StateId).ExecuteDeleteAsync(Token);

        var fresh = await new MediaDimensionsStart(scheduler, media.Host.Time).RunAsync(database, Token);
        var claimed = await new MediaDimensionsStart(scheduler, media.Host.Time).RunAsync(database, Token);
        media.Host.Time.Advance(MediaDimensionsJob.Lease + TimeSpan.FromMinutes(1));
        var stale = await new MediaDimensionsStart(scheduler, media.Host.Time).RunAsync(database, Token);
        var pending = await PendingAsync();
        await RunJobsAsync();
        await StateAsync(MediaDimensionsJob.Done);
        media.Host.Time.Advance(MediaDimensionsJob.Lease + TimeSpan.FromMinutes(1));
        var done = await new MediaDimensionsStart(scheduler, media.Host.Time).RunAsync(database, Token);

        (fresh, claimed, stale, done).ShouldBe((true, false, true, false));
        pending.ShouldBe(2);
        (await PendingAsync()).ShouldBe(0);
    }

    // The database fails the enqueue once, after the claim: the start tries the enqueue again while it holds the claim,
    // rather than leave a claim that no run carries.
    [Fact]
    public async Task AnEnqueueThatFailsIsTriedAgainAndTheChainStarts()
    {
        await StartedAsync();
        await using var scope = media.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        await database.Set<SigningKey>().Where(row => row.Id == MediaDimensionsJob.StateId).ExecuteDeleteAsync(Token);
        var start = new MediaDimensionsStart(new FailingOnce(media.Services.GetRequiredService<IJobScheduler>()), media.Host.Time);

        await Should.ThrowAsync<InvalidOperationException>(() => start.RunAsync(database, Token));
        var retried = await start.RunAsync(database, Token);

        retried.ShouldBeTrue();
        (await PendingAsync()).ShouldBe(1);
        await RunJobsAsync();
        (await StateAsync()).ShouldBe(MediaDimensionsJob.Done);
    }

    // A file that is missing and one whose bytes the processor does not take keep their size, each logged with 9276, and
    // the run goes on to the image after them and to its end.
    [Fact]
    public async Task AMissingOrUnreadableFileIsSkippedAndTheRunGoesOn()
    {
        await StartedAsync();
        using var admin = await media.AdminAsync();
        var missing = await UploadAsync(admin, TurnedJpeg(400, 100), "missing.jpg");
        var broken = await UploadAsync(admin, TurnedJpeg(400, 100), "broken.jpg");
        var good = await UploadAsync(admin, TurnedJpeg(400, 100), "good.jpg");
        foreach (var asset in (MediaAsset[])[missing, broken, good])
        {
            await StoreAsync(asset.Id, 400, 100);
        }

        File.Delete(Path.Combine(media.StorageRoot, (await RowsAsync(missing.Id)).Single().StorageKey));
        await File.WriteAllBytesAsync(Path.Combine(media.StorageRoot, (await RowsAsync(broken.Id)).Single().StorageKey), [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. new byte[64]], Token);
        await StateAsync("");
        var logs = new CapturedLogs();

        await ExecuteAsync(logs: logs);

        (await RowsAsync(missing.Id, broken.Id, good.Id)).Select(asset => (asset.Width ?? 0, asset.Height ?? 0)).ShouldBe([(400, 100), (400, 100), (100, 400)]);
        logs.Lines.Count(line => line.Contains(" 9276: ", StringComparison.Ordinal) && (line.Contains(missing.Id.ToString("D"), StringComparison.Ordinal) || line.Contains(broken.Id.ToString("D"), StringComparison.Ordinal))).ShouldBe(2);
        (await StateAsync()).ShouldBe(MediaDimensionsJob.Done);
    }

    // The storage fails: the run fails, so that the runner tries the batch again, and the cursor stays where it was.
    [Fact]
    public async Task AFailureOfTheStorageFailsTheRunAndTheCursorStays()
    {
        await StartedAsync();
        using var admin = await media.AdminAsync();
        var asset = await UploadAsync(admin, TurnedJpeg(400, 100), "unreachable.jpg");
        await StoreAsync(asset.Id, 400, 100);
        await StateAsync("");
        var key = (await RowsAsync(asset.Id)).Single().StorageKey;

        await Should.ThrowAsync<IOException>(() => ExecuteAsync(storage: new UnreachableStorage(media.Services.GetRequiredService<IFileStorage>(), key)));

        (await StateAsync()).ShouldBe("");
        (await RowsAsync(asset.Id)).Single().Width.ShouldBe(400);
        await ExecuteAsync();
        (await RowsAsync(asset.Id)).Single().Width.ShouldBe(100);
    }

    // A replace saved between the read of the image and the write of its size keeps the size of its new file.
    [Fact]
    public async Task AReplaceBetweenTheReadAndTheWriteKeepsTheSizeOfTheNewFile()
    {
        await StartedAsync();
        using var admin = await media.AdminAsync();
        var asset = await UploadAsync(admin, TurnedJpeg(400, 100), "raced.jpg");
        await StoreAsync(asset.Id, 400, 100);
        await StateAsync("");
        var processor = new WatchedProcessor(media.Services.GetRequiredService<IImageProcessor>())
        {
            Identifying = async () =>
            {
                await using var scope = media.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>()
                    .Where(row => row.Id == asset.Id && row.Checksum == asset.Checksum)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Checksum, new string('f', 64)).SetProperty(row => row.Width, 7).SetProperty(row => row.Height, 9), Token);
            },
        };

        await ExecuteAsync(processor);

        (await RowsAsync(asset.Id)).Single().ShouldSatisfyAllConditions(row => row.Width.ShouldBe(7), row => row.Height.ShouldBe(9));
    }

    // A JPEG over the prefix is identified from the prefix; one whose frame header lies past it, behind large comments, is
    // read whole.
    [Fact]
    public async Task AJpegIsIdentifiedFromItsPrefixAndReadWholeWhenItsHeaderLiesPastIt()
    {
        await StartedAsync();
        using var admin = await media.AdminAsync();
        var turned = TurnedJpeg(400, 100);
        byte[] trailing = [.. turned, .. new byte[MediaDimensionsJob.PrefixLength + (MediaDimensionsJob.PrefixLength / 2)]];
        var exif = 2 + 2 + ((turned[4] << 8) | turned[5]);
        byte[] comment = [0xFF, 0xFE, 0xFF, 0xFF, .. Enumerable.Repeat((byte)'x', 0xFFFF - 2)];
        byte[] buried = [.. turned[..exif], .. Enumerable.Range(0, (MediaDimensionsJob.PrefixLength / comment.Length) + 2).SelectMany(_ => comment), .. turned[exif..]];
        var trailed = await UploadAsync(admin, trailing, "trailing.jpg");
        var deep = await UploadAsync(admin, buried, "buried.jpg");
        await StoreAsync(trailed.Id, 400, 100);
        await StoreAsync(deep.Id, 400, 100);
        await StateAsync("");
        var processor = new WatchedProcessor(media.Services.GetRequiredService<IImageProcessor>());

        await ExecuteAsync(processor);

        (await RowsAsync(trailed.Id, deep.Id)).Select(asset => (asset.Width ?? 0, asset.Height ?? 0)).ShouldBe([(100, 400), (100, 400)]);
        processor.Lengths.ShouldNotContain(trailing.Length);
        processor.Lengths.ShouldContain(buried.Length);
        processor.Lengths.Count(length => length == MediaDimensionsJob.PrefixLength).ShouldBeGreaterThanOrEqualTo(2);
    }

    // A processor that throws on the bytes of an upload fails nothing: the asset takes the size of the header.
    [Fact]
    public async Task AProcessorThatThrowsLeavesTheUploadWithTheSizeOfTheHeader()
    {
        await using var host = new MediaHost();
        host.Host.Configure(cms => cms.Providers.Use<IImageProcessor, ThrowingProcessor>());
        using var admin = await host.AdminAsync();

        var asset = await UploadAsync(admin, TurnedJpeg(400, 100), "thrown.jpg");

        (asset.Width, asset.Height).ShouldBe((400, 100));
    }

    private async Task StartedAsync()
    {
        await media.Services.GetServices<IHostedService>().OfType<MediaStartup>().Single().ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        await RunJobsAsync();
    }

    // One run of the check over the rest of the library, with the processor, storage and logs given.
    private async Task ExecuteAsync(IImageProcessor? processor = null, IFileStorage? storage = null, CapturedLogs? logs = null)
    {
        await using var scope = media.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs ?? new CapturedLogs()));
        var job = new MediaDimensionsJob(
            services.GetRequiredService<PluginDbContext<MediaPlugin>>(),
            storage is null ? services.GetRequiredService<MediaStorage>() : new MediaStorage(storage, services),
            processor ?? services.GetRequiredService<IImageProcessor>(),
            services.GetRequiredService<MediaCache>(),
            media.Services.GetRequiredService<IJobScheduler>(),
            media.Host.Time,
            factory.CreateLogger<MediaDimensionsJob>());
        var operation = new OperationContext(Principal.System, Origin.Job, new SiteId(DefaultSite.Id), "", null, Guid.NewGuid().ToString("N"), media.Host.Time.GetUtcNow());
        await job.ExecuteAsync(new MediaDimensionsPayload(10_000), new JobContext(Guid.NewGuid(), MediaDimensionsJob.Type, JobRunnerOptions.DefaultQueue, 1, 1, operation, Token));
    }

    private async Task<int> PendingAsync()
    {
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking()
            .CountAsync(job => job.Type == MediaDimensionsJob.Type && job.Status == JobClaimStatus.Pending, Token);
    }

    private async Task<string?> StateAsync(string? set = null)
    {
        await using var scope = media.Services.CreateAsyncScope();
        var states = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<SigningKey>().Where(row => row.Id == MediaDimensionsJob.StateId);
        if (set is not null)
        {
            await states.ExecuteUpdateAsync(row => row.SetProperty(item => item.Key, set), Token);
        }

        return await states.Select(row => row.Key).SingleOrDefaultAsync(Token);
    }

    // The size an image had before it was read through the processor: that of its header, before the orientation.
    private async Task StoreAsync(Guid id, int width, int height, string mimeType = "image/jpeg")
    {
        await using var scope = media.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().Where(asset => asset.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(asset => asset.Width, width).SetProperty(asset => asset.Height, height).SetProperty(asset => asset.MimeType, mimeType), Token);
    }

    private async Task<List<Asset>> RowsAsync(params Guid[] ids)
    {
        await using var scope = media.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking().Where(asset => ids.Contains(asset.Id)).ToListAsync(Token);
        return [.. ids.Select(id => rows.Single(asset => asset.Id == id))];
    }

    private static async Task<(int, int)> DeliveredAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/delivery/v1/media/{id:D}", UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var asset = await JsonAsync(response);
        return (asset["width"]!.GetValue<int>(), asset["height"]!.GetValue<int>());
    }

    private sealed class FailingOnce(IJobScheduler inner) : IJobScheduler
    {
        private int _failed;

        public Task<Guid> EnqueueAsync<TJob, TPayload>(TPayload payload, JobOptions? options = null, CancellationToken cancellationToken = default)
            where TJob : class, IJob<TPayload> =>
            Interlocked.Exchange(ref _failed, 1) == 0 ? throw new InvalidOperationException("The database is out of reach.") : inner.EnqueueAsync<TJob, TPayload>(payload, options, cancellationToken);

        public Task<Guid> ScheduleAsync<TJob, TPayload>(TPayload payload, DateTimeOffset runAt, JobOptions? options = null, CancellationToken cancellationToken = default)
            where TJob : class, IJob<TPayload> => inner.ScheduleAsync<TJob, TPayload>(payload, runAt, options, cancellationToken);

        public Task AddRecurringAsync<TJob, TPayload>(string name, string cron, TPayload payload, string? queue, CancellationToken cancellationToken = default)
            where TJob : class, IJob<TPayload> => inner.AddRecurringAsync<TJob, TPayload>(name, cron, payload, queue, cancellationToken);
    }

    // The local disk, which fails every read of one key as an unreachable storage does.
    private sealed class UnreachableStorage(IFileStorage inner, string key) : IFileStorage
    {
        public string Name => inner.Name;

        public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
            path == key ? throw new IOException("The storage is out of reach.") : inner.OpenReadAsync(path, cancellationToken);

        public Task WriteAsync(string path, Stream content, CancellationToken cancellationToken = default) => inner.WriteAsync(path, content, cancellationToken);

        public Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default) => inner.DeleteAsync(path, cancellationToken);

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => inner.ExistsAsync(path, cancellationToken);

        public Task<FileStorageMetadata?> GetMetadataAsync(string path, CancellationToken cancellationToken = default) => inner.GetMetadataAsync(path, cancellationToken);
    }

    // The processor of the host, which records the length of each stream it identifies and runs Identifying first.
    private sealed class WatchedProcessor(IImageProcessor inner) : IImageProcessor
    {
        public List<long> Lengths { get; } = [];

        public Func<Task>? Identifying { get; init; }

        public bool CanDecode(string mimeType) => inner.CanDecode(mimeType);

        public bool CanEncode(string mimeType) => inner.CanEncode(mimeType);

        public async Task<ImageHeader?> IdentifyAsync(Stream image, CancellationToken cancellationToken = default)
        {
            Lengths.Add(image.Length);
            if (Identifying is { } identifying)
            {
                await identifying();
            }

            return await inner.IdentifyAsync(image, cancellationToken);
        }

        public Task ProcessAsync(Stream image, ImageTransform transform, Stream output, CancellationToken cancellationToken = default) => inner.ProcessAsync(image, transform, output, cancellationToken);
    }

    private async Task<int> RunJobsAsync()
    {
        var runs = 0;
        await using var scope = media.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
        while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
        {
            runs++;
        }

        return runs;
    }
}

internal sealed class ThrowingProcessor : IImageProcessor
{
    public bool CanDecode(string mimeType) => mimeType == "image/jpeg";

    public bool CanEncode(string mimeType) => false;

    public Task<ImageHeader?> IdentifyAsync(Stream image, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The processor failed.");

    public Task ProcessAsync(Stream image, ImageTransform transform, Stream output, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The processor failed.");
}
