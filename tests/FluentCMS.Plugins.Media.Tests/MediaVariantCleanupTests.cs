using System.Net;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaVariantCleanupTests
{
    [Fact]
    public async Task TheCleanupRemovesTheVariantsOfDeletedAssetsFromTheStorageAndTheTable()
    {
        await using var media = new MediaHost();
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

        var before = await VariantsAsync(media);
        await media.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaVariantCleanupJob, MediaVariantCleanupPayload>(new MediaVariantCleanupPayload(), cancellationToken: Token);
        await using (var scope = media.Services.CreateAsyncScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
            while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
            {
            }
        }

        var after = await VariantsAsync(media);
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
        await using var media = new MediaHost();
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

        (await VariantsAsync(media)).Select(variant => variant.AssetId).ShouldBe([foreign.Id]);
    }

    [Fact]
    public async Task TheCleanupRunsDailyOnceTheHostHasStarted()
    {
        await using var media = new MediaHost();

        RecurringJob? schedule = null;
        for (var attempt = 0; attempt < 400 && schedule is null; attempt++)
        {
            await using var scope = media.Services.CreateAsyncScope();
            schedule = await scope.ServiceProvider.GetRequiredService<Core.Storage.SystemDbContext>().Set<RecurringJob>()
                .AsNoTracking()
                .FirstOrDefaultAsync(job => job.Name == MediaVariantCleanupJob.Type, Token);
            if (schedule is null)
            {
                await Task.Delay(50, Token);
            }
        }

        schedule.ShouldNotBeNull();
        (schedule.Type, schedule.Cron).ShouldBe((MediaVariantCleanupJob.Type, MediaVariantCleanupJob.Schedule));
    }

    private static async Task<List<Variant>> VariantsAsync(MediaHost media)
    {
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Variant>().AsNoTracking().OrderBy(variant => variant.AssetId).ToListAsync(Token);
    }
}
