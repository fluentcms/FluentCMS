using System.Net;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage.Entities;
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

public sealed class MediaVariantCleanupTests(MediaHost media) : IClassFixture<MediaHost>
{
    [Fact]
    public async Task TheCleanupRemovesTheVariantsOfDeletedAssetsFromTheStorageAndTheTable()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var kept = await UploadAsync(admin, Bands(40, 20));
        var deleted = await UploadAsync(admin, Bands(40, 20));
        foreach (var asset in (MediaAsset[])[kept, deleted])
        {
            foreach (var width in (int[])[10, 20])
            {
                using var response = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Width = width }));
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
            }
        }

        using (var delete = await admin.DeleteAsync(new Uri($"{Root}/{deleted.Id:D}", UriKind.Relative), Token))
        {
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var before = await VariantsAsync(kept, deleted);
        await media.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaVariantCleanupJob, MediaVariantCleanupPayload>(new MediaVariantCleanupPayload(), cancellationToken: Token);
        await using (var scope = media.Services.CreateAsyncScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
            while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
            {
            }
        }

        var after = await VariantsAsync(kept, deleted);
        before.Count.ShouldBe(4);
        after.Select(variant => variant.AssetId).ShouldBe([kept.Id, kept.Id]);
        var storage = media.Services.GetRequiredService<IFileStorage>();
        foreach (var variant in before)
        {
            (await storage.ExistsAsync(variant.StorageKey, Token)).ShouldBe(variant.AssetId == kept.Id);
        }

        Directory.EnumerateFiles(Path.Combine(media.StorageRoot, deleted.SiteId.ToString("N"), deleted.Id.ToString("N"))).Count().ShouldBe(1);
    }

    [Fact]
    public async Task AVariantInAnotherStorageKeepsItsRowAndTheRestAreRemoved()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var foreign = await UploadAsync(admin, Bands(40, 20));
        var local = await UploadAsync(admin, Bands(40, 20));
        foreach (var asset in (MediaAsset[])[foreign, local])
        {
            using (var response = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Width = 10 })))
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            using var delete = await admin.DeleteAsync(new Uri($"{Root}/{asset.Id:D}", UriKind.Relative), Token);
            delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await using (var scope = media.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>()
                .Where(asset => asset.Id == foreign.Id)
                .ExecuteUpdateAsync(item => item.SetProperty(asset => asset.StorageProvider, "elsewhere"), Token);
        }

        await media.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaVariantCleanupJob, MediaVariantCleanupPayload>(new MediaVariantCleanupPayload(), cancellationToken: Token);
        await using (var scope = media.Services.CreateAsyncScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
            while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
            {
            }
        }

        (await VariantsAsync(foreign, local)).Select(variant => variant.AssetId).ShouldBe([foreign.Id]);
    }

    [Fact]
    public async Task TheCleanupPurgesTheAssetsDeletedLongerAgoThanTheRetentionWithTheirFiles()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var old = await UploadAsync(admin, Bands(8, 8));
        var recent = await UploadAsync(admin, Bands(8, 8));
        var kept = await UploadAsync(admin, Bands(8, 8));
        using (var response = await FetchAsync(anonymous, await UrlAsync(media, old, new MediaImageOptions { Width = 4 })))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await admin.DeleteAsync(new Uri($"{Root}/{old.Id:D}", UriKind.Relative), Token)).Dispose();
        media.Host.Time.Advance(TimeSpan.FromDays(20));
        (await admin.DeleteAsync(new Uri($"{Root}/{recent.Id:D}", UriKind.Relative), Token)).Dispose();
        media.Host.Time.Advance(TimeSpan.FromDays(10) + TimeSpan.FromSeconds(1));

        await media.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaVariantCleanupJob, MediaVariantCleanupPayload>(new MediaVariantCleanupPayload(), cancellationToken: Token);
        await using (var scope = media.Services.CreateAsyncScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
            while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
            {
            }
        }

        await using var check = media.Services.CreateAsyncScope();
        Guid[] own = [old.Id, recent.Id, kept.Id];
        var left = await check.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking().Where(asset => own.Contains(asset.Id)).Select(asset => asset.Id).ToListAsync(Token);
        left.ShouldBe([recent.Id, kept.Id], ignoreOrder: true);
        (await VariantsAsync(old, recent, kept)).ShouldBeEmpty();
        Directory.EnumerateFiles(Path.Combine(media.StorageRoot, old.SiteId.ToString("N"), old.Id.ToString("N"))).ShouldBeEmpty();
        Directory.EnumerateFiles(Path.Combine(media.StorageRoot, recent.SiteId.ToString("N"), recent.Id.ToString("N"))).Count().ShouldBe(1);
    }

    [Fact]
    public async Task TheCleanupRunsDailyOnceTheHostHasStarted()
    {
        // The schedule is stored by the media startup, which runs once the host has started and then ends.
        await media.Services.GetServices<IHostedService>().OfType<MediaStartup>().Single().ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30), Token);

        await using var scope = media.Services.CreateAsyncScope();
        var schedule = await scope.ServiceProvider.GetRequiredService<Core.Storage.SystemDbContext>().Set<RecurringJob>()
            .AsNoTracking()
            .FirstOrDefaultAsync(job => job.Name == MediaVariantCleanupJob.Type, Token);

        schedule.ShouldNotBeNull();
        (schedule.Type, schedule.Cron).ShouldBe((MediaVariantCleanupJob.Type, MediaVariantCleanupJob.Schedule));
    }

    private async Task<List<Variant>> VariantsAsync(params MediaAsset[] assets)
    {
        var ids = assets.Select(asset => asset.Id).ToList();
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Variant>().AsNoTracking()
            .Where(variant => ids.Contains(variant.AssetId)).OrderBy(variant => variant.AssetId).ToListAsync(Token);
    }
}
