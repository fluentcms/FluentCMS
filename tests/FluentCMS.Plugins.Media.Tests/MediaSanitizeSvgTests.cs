using System.Security.Cryptography;
using FluentCMS.Content;
using FluentCMS.Core.Commands;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Jobs;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

// `fluentcms media sanitize-svg` (#3605) over SVGs stored by an older pass: an upload through the current one whose stored
// file and row are then set to what an older build stored. Each test has a host of its own, since the command reads every
// library of the instance.
public sealed class MediaSanitizeSvgTests
{
    private static readonly TimeSpan PastRetention = new MediaOptions().DeletedRetention + TimeSpan.FromDays(1);

    // A <style> element and an id without a prefix, which the current pass takes out and scopes.
    private static readonly byte[] Legacy = Files.Svg("""<style>rect{fill:red}</style><rect id="dot" width="5" height="5"/>""");

    [Fact]
    public async Task ADryRunListsTheSvgsThePassWouldChangeAndChangesNothing()
    {
        await using var media = new PerSiteMediaHost();
        using var admin = await media.AdminAsync();
        await ConfigureAsync(media, DefaultSite.Id, settings => settings.Svg = SvgHandling.Sanitize);
        var legacy = await LegacyAsync(media, admin, Legacy);
        var current = await UploadSvgAsync(admin, Legacy);
        var deleted = await LegacyAsync(media, admin, Legacy);
        (await admin.DeleteAsync(new Uri($"{Root}/{deleted:D}", UriKind.Relative), Token)).Dispose();
        var before = await RowAsync(media, legacy);

        var result = await media.Host.RunCommandAsync("media", "sanitize-svg", "--dry-run");

        result.ExitCode.ShouldBe(0, result.Error);
        result.Output.ShouldContain($"Would sanitise logo.svg (asset {legacy:D} of site {DefaultSite.Id:D})");
        result.Output.ShouldNotContain(current.ToString("D"));
        result.Output.ShouldNotContain(deleted.ToString("D"));
        result.Output.ShouldContain("1 stored SVGs would be sanitised again and 1 are as the pass writes them; 0 would be left as they are");
        result.Output.ShouldContain("Nothing was changed.");
        var after = await RowAsync(media, legacy);
        (after.StorageKey, after.Size, after.Checksum, after.Version).ShouldBe((before.StorageKey, before.Size, before.Checksum, before.Version));
        (await File.ReadAllBytesAsync(Path.Combine(media.StorageRoot, before.StorageKey), Token)).ShouldBe(Legacy);
    }

    [Fact]
    public async Task TheRunReplacesEachChangedSvgUnderAKeyOfItsOwnWithTheSanitisedBytes()
    {
        await using var media = new PerSiteMediaHost();
        using var admin = await media.AdminAsync();
        await ConfigureAsync(media, DefaultSite.Id, settings => settings.Svg = SvgHandling.Sanitize);
        var id = await LegacyAsync(media, admin, Legacy);
        var before = await RowAsync(media, id);
        var sanitized = SvgDocument.Sanitized(new MemoryStream(Legacy), Legacy.Length, Token).ShouldNotBeNull();

        var result = await media.Host.RunCommandAsync("media", "sanitize-svg");

        result.ExitCode.ShouldBe(0, result.Error);
        var after = await RowAsync(media, id);
        after.StorageKey.ShouldNotBe(before.StorageKey);
        after.StorageKey.ShouldStartWith($"{DefaultSite.Id:N}/{id:N}/original-");
        after.StorageKey.ShouldEndWith(".svg");
        after.OwnsFile.ShouldBeTrue();
        after.Size.ShouldBe(sanitized.Length);
        after.Checksum.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(sanitized)));
        after.Checksum.ShouldNotBe(before.Checksum);
        after.Version.ShouldBe(before.Version + 1);
        (after.FileName, after.MimeType).ShouldBe(("logo.svg", "image/svg+xml"));
        (await File.ReadAllBytesAsync(Path.Combine(media.StorageRoot, after.StorageKey), Token)).ShouldBe(sanitized);
        File.Exists(Path.Combine(media.StorageRoot, before.StorageKey)).ShouldBeFalse();
        result.Output.ShouldContain($"Sanitised logo.svg (asset {id:D} of site {DefaultSite.Id:D}): {Legacy.Length} bytes, {sanitized.Length} after, checksum {after.Checksum}, version {after.Version}.");
        result.Output.ShouldContain("Sanitised 1 stored SVGs again and 0 were as the pass writes them; 0 are left as they are");
        result.Output.ShouldContain("Their checksums changed, and so did the v of their URLs.");
        result.Output.ShouldContain("a CDN serves the old bytes under it until its copy expires (FluentCms:Media:CacheControl)");
    }

    [Fact]
    public async Task EachReplacedSvgRaisesOneReplacedEventAndADryRunNone()
    {
        await using var media = new PerSiteMediaHost();
        using var admin = await media.AdminAsync();
        await ConfigureAsync(media, DefaultSite.Id, settings => settings.Svg = SvgHandling.Sanitize);
        var first = await LegacyAsync(media, admin, Legacy);
        var second = await LegacyAsync(media, admin, Files.Svg("""<style>circle{fill:blue}</style><circle id="c" r="2"/>"""));
        var current = await UploadSvgAsync(admin, Legacy);

        (await media.Host.RunCommandAsync("media", "sanitize-svg", "--dry-run")).ExitCode.ShouldBe(0);
        await media.DrainOutboxAsync();
        var afterDryRun = await ReplacedEventsAsync(admin, first, second, current);
        (await media.Host.RunCommandAsync("media", "sanitize-svg")).ExitCode.ShouldBe(0);
        await media.DrainOutboxAsync();

        afterDryRun.ShouldBe([0, 0, 0]);
        (await ReplacedEventsAsync(admin, first, second, current)).ShouldBe([1, 1, 0]);
    }

    // The owner's old file is a key another asset names, as an import leaves one. Replaced first, the owner gives it to a
    // holder, which keeps it until the cleanup purges the holder once no other names it; replaced after the other, which
    // takes a key of its own first, no other names it and it goes with the replace.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnOldFileAnotherAssetNamesIsKeptUntilNoneNamesIt(bool namedFirst)
    {
        await using var media = new PerSiteMediaHost();
        using var admin = await media.AdminAsync();
        await ConfigureAsync(media, DefaultSite.Id, settings => settings.Svg = SvgHandling.Sanitize);
        var owner = await LegacyAsync(media, admin, Legacy);
        var key = (await RowAsync(media, owner)).StorageKey;
        var named = await NameAsync(media, owner, namedFirst ? Guid.CreateVersion7(DateTimeOffset.UnixEpoch) : Guid.CreateVersion7(DateTimeOffset.MaxValue.AddYears(-1)));

        var result = await media.Host.RunCommandAsync("media", "sanitize-svg");

        result.ExitCode.ShouldBe(0, result.Error);
        result.Output.ShouldContain($"asset {owner:D}");
        result.Output.ShouldContain($"asset {named:D}");
        (await RowAsync(media, owner)).StorageKey.ShouldNotBe(key);
        (await RowAsync(media, named)).StorageKey.ShouldNotBe(key);
        if (namedFirst)
        {
            File.Exists(Path.Combine(media.StorageRoot, key)).ShouldBeFalse();
            (await HolderAsync(media, key)).ShouldBeNull();
            return;
        }

        var holder = (await HolderAsync(media, key)).ShouldNotBeNull();
        File.Exists(Path.Combine(media.StorageRoot, key)).ShouldBeTrue();
        await CleanupAsync(media);
        File.Exists(Path.Combine(media.StorageRoot, key)).ShouldBeTrue();

        media.Host.Time.Advance(PastRetention);
        await CleanupAsync(media);

        File.Exists(Path.Combine(media.StorageRoot, key)).ShouldBeFalse();
        (await FindAsync(media, holder.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task AnSvgOverTheSitesSvgLimitIsListedAndNotReplacedInTheDryRunAndTheRun()
    {
        await using var media = new PerSiteMediaHost();
        using var admin = await media.AdminAsync();
        await ConfigureAsync(media, DefaultSite.Id, settings => settings.Svg = SvgHandling.Sanitize);
        var large = Files.Svg("""<style>rect{fill:red}</style><rect id="dot" width="5" height="5"/>""" + new string(' ', 400));
        var over = await LegacyAsync(media, admin, large);
        var under = await LegacyAsync(media, admin, Legacy);
        await ConfigureAsync(media, DefaultSite.Id, settings => settings.MaxSvgFileSize = Legacy.Length + 10);
        var before = await RowAsync(media, over);

        var dryRun = await media.Host.RunCommandAsync("media", "sanitize-svg", "--dry-run");
        var run = await media.Host.RunCommandAsync("media", "sanitize-svg");

        foreach (var result in (TestCommandResult[])[dryRun, run])
        {
            result.ExitCode.ShouldBe(1);
            result.Error.ShouldContain($"Left logo.svg (asset {over:D} of site {DefaultSite.Id:D}), of {large.Length} bytes, as it is: it is over the site's maxSvgFileSize of {Legacy.Length + 10} bytes, so it was not parsed");
            result.Error.ShouldContain($"{CommandErrorCodes.SvgsNotSanitized}: 1 stored SVGs are left as they are");
            result.Output.ShouldContain("1 over maxSvgFileSize, 0 that the pass refuses");
        }

        run.Output.ShouldContain($"asset {under:D}");
        var after = await RowAsync(media, over);
        (after.StorageKey, after.Checksum, after.Version).ShouldBe((before.StorageKey, before.Checksum, before.Version));
        (await File.ReadAllBytesAsync(Path.Combine(media.StorageRoot, after.StorageKey), Token)).ShouldBe(large);
    }

    [Fact]
    public async Task AFileThePassNoLongerTakesAsAnSvgIsListedAndNeitherReplacedNorDeleted()
    {
        await using var media = new PerSiteMediaHost();
        using var admin = await media.AdminAsync();
        await ConfigureAsync(media, DefaultSite.Id, settings => settings.Svg = SvgHandling.Sanitize);
        byte[] markup = [.. "<div>no drawing</div>"u8];
        var id = await LegacyAsync(media, admin, markup);
        var before = await RowAsync(media, id);

        var result = await media.Host.RunCommandAsync("media", "sanitize-svg");

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain($"Left logo.svg (asset {id:D} of site {DefaultSite.Id:D}) as it is: the pass no longer takes it as an SVG");
        result.Output.ShouldContain("0 over maxSvgFileSize, 1 that the pass refuses");
        var after = await RowAsync(media, id);
        (after.StorageKey, after.Version, after.DeletedAt).ShouldBe((before.StorageKey, before.Version, (DateTimeOffset?)null));
        (await File.ReadAllBytesAsync(Path.Combine(media.StorageRoot, after.StorageKey), Token)).ShouldBe(markup);
    }

    [Fact]
    public async Task ASecondRunChangesNothing()
    {
        await using var media = new PerSiteMediaHost();
        using var admin = await media.AdminAsync();
        await ConfigureAsync(media, DefaultSite.Id, settings => settings.Svg = SvgHandling.Sanitize);
        var id = await LegacyAsync(media, admin, Legacy);
        (await media.Host.RunCommandAsync("media", "sanitize-svg")).ExitCode.ShouldBe(0);
        var first = await RowAsync(media, id);
        await media.DrainOutboxAsync();

        var second = await media.Host.RunCommandAsync("media", "sanitize-svg");
        await media.DrainOutboxAsync();

        second.ExitCode.ShouldBe(0, second.Error);
        second.Output.ShouldNotContain(id.ToString("D"));
        second.Output.ShouldContain("Sanitised 0 stored SVGs again and 1 were as the pass writes them; 0 are left as they are");
        second.Output.ShouldNotContain("checksums changed");
        var after = await RowAsync(media, id);
        (after.StorageKey, after.Checksum, after.Version, after.UpdatedAt).ShouldBe((first.StorageKey, first.Checksum, first.Version, first.UpdatedAt));
        (await ReplacedEventsAsync(admin, id)).ShouldBe([1]);
    }

    [Fact]
    public async Task EverySitesLibraryIsSanitisedWithItsOwnSettings()
    {
        await using var media = new PerSiteMediaHost();
        using var admin = await media.AdminAsync();
        var site = await MediaLibraryTesting.CreateSiteAsync(media);
        using var inSite = await MediaLibraryTesting.InSiteAsync(media, site);
        await ConfigureAsync(media, DefaultSite.Id, settings => settings.Svg = SvgHandling.Sanitize);
        await ConfigureAsync(media, site, settings => settings.Svg = SvgHandling.Sanitize);
        var own = await LegacyAsync(media, admin, Legacy);
        var other = await LegacyAsync(media, inSite, Legacy);
        await ConfigureAsync(media, site, settings => settings.MaxSvgFileSize = Legacy.Length - 1);

        var limited = await media.Host.RunCommandAsync("media", "sanitize-svg");
        await ConfigureAsync(media, site, settings => settings.MaxSvgFileSize = Legacy.Length);
        var again = await media.Host.RunCommandAsync("media", "sanitize-svg");

        limited.ExitCode.ShouldBe(1);
        limited.Output.ShouldContain($"Sanitised logo.svg (asset {own:D} of site {DefaultSite.Id:D})");
        limited.Error.ShouldContain($"Left logo.svg (asset {other:D} of site {site:D}), of {Legacy.Length} bytes, as it is");
        again.ExitCode.ShouldBe(0, again.Error);
        again.Output.ShouldContain($"Sanitised logo.svg (asset {other:D} of site {site:D})");
        again.Output.ShouldNotContain(own.ToString("D"));
        (await RowAsync(media, own)).Version.ShouldBe(2);
        var replaced = await RowAsync(media, other);
        (replaced.SiteId, replaced.Version).ShouldBe((site, 2));
        replaced.StorageKey.ShouldStartWith($"{site:N}/{other:N}/original-");
    }

    private static async Task<Guid> UploadSvgAsync(HttpClient client, byte[] bytes) =>
        Id((await UploadedAsync(client, null, ("logo.svg", bytes))).Single()!);

    // An SVG uploaded through the current pass, whose file and row are then those of the bytes given, as an older build
    // stored them.
    private static async Task<Guid> LegacyAsync(MediaHost media, HttpClient client, byte[] stored)
    {
        var id = await UploadSvgAsync(client, Files.Svg("""<rect width="5" height="5"/>"""));
        await using var scope = media.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var row = await database.Set<Asset>().SingleAsync(asset => asset.Id == id, Token);
        await File.WriteAllBytesAsync(Path.Combine(media.StorageRoot, row.StorageKey), stored, Token);
        row.Size = stored.Length;
        row.Checksum = Convert.ToHexStringLower(SHA256.HashData(stored));
        await database.SaveChangesAsync(Token);
        return id;
    }

    // Another asset on the owner's key, as an import leaves one.
    private static async Task<Guid> NameAsync(MediaHost media, Guid owner, Guid named)
    {
        await using var scope = media.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var row = await database.Set<Asset>().AsNoTracking().SingleAsync(asset => asset.Id == owner, Token);
        row.Id = named;
        row.OwnsFile = false;
        database.Add(row);
        await database.SaveChangesAsync(Token);
        return named;
    }

    private static async Task ConfigureAsync(MediaHost media, Guid site, Action<MediaSettings> change)
    {
        await using var scope = media.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using (SystemOperation.Begin(services.GetRequiredService<IOperationContextAccessor>(), services.GetRequiredService<TimeProvider>(), Origin.Seed, new SiteId(site)))
        {
            var settings = services.GetRequiredService<ISettings<MediaSettings>>();
            var current = await settings.GetAsync(new SiteId(site), Token);
            change(current);
            await settings.SetAsync(new SiteId(site), current, Token);
        }
    }

    private static async Task<List<int>> ReplacedEventsAsync(HttpClient admin, params Guid[] ids)
    {
        List<int> counts = [];
        foreach (var id in ids)
        {
            counts.Add((await GetAsync(admin, $"/api/management/v1/activity?targetType=media&targetId={id:D}&action=media.replaced"))["items"]!.AsArray().Count);
        }

        return counts;
    }

    private static async Task<Asset> RowAsync(MediaHost media, Guid id) => (await FindAsync(media, id)).ShouldNotBeNull();

    private static async Task<Asset?> FindAsync(MediaHost media, Guid id)
    {
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking().FirstOrDefaultAsync(asset => asset.Id == id, Token);
    }

    // The deleted row that owns a file a replace left.
    private static async Task<Asset?> HolderAsync(MediaHost media, string key)
    {
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.StorageKey == key && asset.OwnsFile && asset.DeletedAt != null, Token);
    }

    private static async Task CleanupAsync(MediaHost media)
    {
        await media.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<MediaVariantCleanupJob, MediaVariantCleanupPayload>(new MediaVariantCleanupPayload(), cancellationToken: Token);
        await using var scope = media.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<JobExecutor>();
        while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
        {
        }
    }
}
