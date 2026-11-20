using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentCMS.Core.Jobs;
using FluentCMS.Jobs;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    // Two images stored with the size of their header and one stored right. A run reads one image and enqueues the next, so
    // three runs go through them; nothing but the size changes, and running it again changes nothing.
    [Fact]
    public async Task TheBackfillGivesImagesStoredBeforeTheSizeTheyAreShownAtAndNothingElse()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        await media.Services.GetServices<IHostedService>().OfType<MediaStartup>().Single().ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        await RunJobsAsync();
        var first = await UploadAsync(admin, TurnedJpeg(400, 100), "first.jpg");
        var second = await UploadAsync(admin, TurnedJpeg(200, 50), "second.jpg");
        var plain = await UploadAsync(admin, Bands(40, 20), "plain.png");
        await StoreAsync(first.Id, 400, 100);
        await StoreAsync(second.Id, 200, 50);
        var before = await RowsAsync(first.Id, second.Id, plain.Id);
        (await DeliveredAsync(anonymous, first.Id)).ShouldBe((400, 100));

        await media.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaDimensionsJob, MediaDimensionsPayload>(new MediaDimensionsPayload(null, BatchSize: 1), cancellationToken: Token);
        (await RunJobsAsync()).ShouldBeGreaterThanOrEqualTo(3);

        var after = await RowsAsync(first.Id, second.Id, plain.Id);
        after.Select(asset => (asset.Width ?? 0, asset.Height ?? 0)).ShouldBe([(100, 400), (50, 200), (40, 20)]);
        after.Select(asset => (asset.Version, asset.UpdatedAt, asset.UpdatedBy)).ShouldBe(before.Select(asset => (asset.Version, asset.UpdatedAt, asset.UpdatedBy)));
        (await DeliveredAsync(anonymous, first.Id)).ShouldBe((100, 400));
        await media.DrainOutboxAsync();
        var actions = (await GetAsync(admin, $"/api/management/v1/activity?targetType=media&targetId={first.Id:D}"))["items"]!.AsArray().Select(row => row!["action"]!.GetValue<string>());
        actions.ShouldBe(["media.uploaded"]);

        await media.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaDimensionsJob, MediaDimensionsPayload>(new MediaDimensionsPayload(null), cancellationToken: Token);
        await RunJobsAsync();

        (await RowsAsync(first.Id, second.Id, plain.Id)).Select(asset => (asset.Width, asset.Height, asset.Version)).ShouldBe(after.Select(asset => (asset.Width, asset.Height, asset.Version)));
    }

    // The size an image had before it was read through the processor: that of its header, before the orientation.
    private async Task StoreAsync(Guid id, int width, int height)
    {
        await using var scope = media.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().Where(asset => asset.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(asset => asset.Width, width).SetProperty(asset => asset.Height, height), Token);
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
