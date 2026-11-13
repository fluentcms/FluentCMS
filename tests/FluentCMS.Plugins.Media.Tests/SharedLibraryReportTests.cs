using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Diagnostics;
using FluentCMS.Plugins.Tests;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

// With FluentCms:Media:Library=Shared, the startup report names the option and the sites whose own assets the shared
// library hides, which a host has once it turns PerSite back to Shared. Two hosts on one database file stand for that.
public sealed class SharedLibraryReportTests : IDisposable
{
    private const string Option = "FluentCms:Media:Library=PerSite";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-media-report-");

    private string Database => Path.Combine(_directory.FullName, "cms.db");

    [Fact]
    public async Task AHostThatTurnsPerSiteBackToSharedNamesTheOptionAndTheSitesAtStart()
    {
        var (north, south, emptied, gone) = await PerSiteLibrariesAsync();
        var logs = new CapturedLogs();

        await using var host = Host(MediaLibrary.Shared, logs);

        var item = Report(host).ShouldHaveSingleItem();
        item.Message.ShouldContain(Option);
        item.Message.ShouldContain($"{north:D} (North)");
        item.Message.ShouldContain($"{south:D} (South)");
        item.Message.ShouldNotContain(emptied.ToString("D"));
        item.Message.ShouldNotContain(gone.ToString("D"));
        item.Message.ShouldNotContain(DefaultSite.Id.ToString("D"));
        item.Message.ShouldNotContain("and more");
        logs.Lines.ShouldContain(line => line.StartsWith($"Warning {typeof(StartupReportService).FullName} {StartupReportEvents.InsecureDefault}: Insecure default {SharedLibraryReport.HiddenSites}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithPerSiteTheReportSaysNothing()
    {
        await PerSiteLibrariesAsync();

        await using var host = Host(MediaLibrary.PerSite);

        Report(host).ShouldBeEmpty();
    }

    // The default site's own assets are the library, and a site with no asset hides nothing.
    [Fact]
    public async Task WithOneSiteTheReportSaysNothing()
    {
        var logs = new CapturedLogs();
        await using var host = Host(MediaLibrary.Shared, logs);
        using (var admin = await StorageHosts.AdminAsync(host))
        {
            await UploadedAsync(admin, null, ("a.png", Files.Png(1, 1)));
        }

        Report(host).ShouldBeEmpty();

        await CreateSiteAsync(host, "Empty");
        (await host.Services.GetRequiredService<StartupReportFactory>().CreateAsync(Token)).InsecureDefaults.ShouldNotContain(item => item.Id == SharedLibraryReport.HiddenSites);
        logs.Lines.ShouldNotContain(line => line.Contains(" 9273:", StringComparison.Ordinal));
    }

    // A check that cannot read the table says nothing and does not fail the report.
    [Fact]
    public async Task ACheckThatCannotRunIsLoggedAndTheReportIsMade()
    {
        var logs = new CapturedLogs();
        await using var host = Host(MediaLibrary.Shared, logs);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Database.ExecuteSqlRawAsync($"DROP TABLE {MediaModel.AssetTable}", Token);
        }

        var report = await host.Services.GetRequiredService<StartupReportFactory>().CreateAsync(Token);

        report.Failure.ShouldBeNull();
        report.InsecureDefaults.ShouldNotContain(item => item.Id == SharedLibraryReport.HiddenSites);
        logs.Lines.ShouldContain(line => line.StartsWith($"Warning {typeof(SharedLibraryReport).FullName} 9273:", StringComparison.Ordinal) && line.Contains($"({nameof(SqliteException)})", StringComparison.Ordinal));
    }

    // The library's own rows lie between the two ranges, which each provider seeks apart, and no row is counted.
    [Theory]
    [MemberData(nameof(Sets))]
    public void TheQueryReadsTwoRangesAroundTheLibraryAndStops(string set)
    {
        using var context = new MediaDbContextFactory().CreateDbContext([set]);

        var sql = SharedLibraryReport.OtherSites(context.Set<Asset>(), Guid.NewGuid()).ToQueryString();

        sql.ShouldContain("UNION");
        sql.ShouldContain(set == PluginMigrationSets.SqlServer ? "TOP(" : "LIMIT ");
        sql.ShouldMatch(@"site_id[\]""]? < @");
        sql.ShouldMatch(@"site_id[\]""]? > @");
        sql.ShouldNotContain("<>");
        sql.ShouldNotContain(" OR ");
        sql.ShouldNotContain("COUNT", Case.Insensitive);
    }

    public static TheoryData<string> Sets => PluginMigrationSetChecks.Sets;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }

    // A host with PerSite whose sites North and South each keep an asset of their own, Emptied only a deleted one, and a
    // site that is gone left a row the outbox has not removed yet. The default site has its own.
    private async Task<(Guid North, Guid South, Guid Emptied, Guid Gone)> PerSiteLibrariesAsync()
    {
        await using var host = Host(MediaLibrary.PerSite);
        var north = await CreateSiteAsync(host, "North");
        var south = await CreateSiteAsync(host, "South");
        var emptied = await CreateSiteAsync(host, "Emptied");
        var gone = Guid.NewGuid();
        await UploadAsync(host, null);
        await UploadAsync(host, north);
        await UploadAsync(host, north);
        await UploadAsync(host, south);
        var deleted = await UploadAsync(host, emptied);
        var orphan = await UploadAsync(host, null);

        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        await database.Set<Asset>().Where(asset => asset.Id == deleted).ExecuteUpdateAsync(set => set.SetProperty(asset => asset.DeletedAt, DateTimeOffset.UtcNow), Token);
        await database.Set<Asset>().Where(asset => asset.Id == orphan).ExecuteUpdateAsync(set => set.SetProperty(asset => asset.SiteId, gone), Token);
        return (north, south, emptied, gone);
    }

    private FluentCmsTestHost Host(MediaLibrary library, CapturedLogs? logs = null) =>
        StorageHosts.Create(_directory.CreateSubdirectory("media"), StorageHosts.SigningKey(), database: Database, logs: logs)
            .WithServices(services => services.PostConfigure<MediaOptions>(options => options.Library = library));

    private static List<InsecureDefault> Report(FluentCmsTestHost host) =>
        [.. host.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Where(item => item.Id == SharedLibraryReport.HiddenSites)];

    private static async Task<Guid> CreateSiteAsync(FluentCmsTestHost host, string name)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync(name, $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}", "en", Token)).Id;
    }

    private static async Task<Guid> UploadAsync(FluentCmsTestHost host, Guid? site)
    {
        using var admin = await StorageHosts.AdminAsync(host);
        if (site is { } one)
        {
            admin.DefaultRequestHeaders.Add("X-Cms-Site", one.ToString());
        }

        return Id((await UploadedAsync(admin, null, ("a.png", Files.Png(1, 1)))).Single()!);
    }
}

// The report names five sites, whatever their number, and says there are more.
public sealed class SharedLibraryReportBoundTests(MediaHost fixture) : IClassFixture<MediaHost>
{
    [Fact]
    public async Task AtMostFiveSitesAreNamed()
    {
        List<Guid> sites = [];
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<SiteManager>();
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
            for (var index = 0; index < SharedLibraryReport.MaxNamed + 2; index++)
            {
                var site = (await manager.CreateAsync($"Site {index}", $"site-{index}-{Guid.NewGuid():N}", "en", Token)).Id;
                sites.Add(site);
                database.Set<Asset>().Add(Row(site));
            }

            await database.SaveChangesAsync(Token);
        }

        var item = fixture.Services.GetServices<IInsecureDefaultSource>().OfType<SharedLibraryReport>().Single().GetInsecureDefaults().ShouldHaveSingleItem();

        sites.Count(site => item.Message.Contains(site.ToString("D"), StringComparison.Ordinal)).ShouldBe(SharedLibraryReport.MaxNamed);
        item.Message.ShouldContain(", and more.");
    }

    // An asset as the library of a site kept it while it was its own.
    internal static Asset Row(Guid site)
    {
        var id = Guid.CreateVersion7();
        return new Asset
        {
            Id = id,
            SiteId = site,
            FileName = "a.png",
            StorageProvider = LocalFileStorage.ProviderName,
            StorageKey = $"{site:N}/{id:N}/original.png",
            MimeType = "image/png",
            Size = 1,
            Checksum = new string('0', 64),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }
}

// A site whose id sorts below the library's is found by the lower of the two ranges, and the control characters of its name,
// which its own administrator chose, do not reach the log or the terminal.
public sealed class SharedLibraryReportNameTests(MediaHost fixture) : IClassFixture<MediaHost>
{
    [Fact]
    public async Task ASiteBelowTheLibraryIsNamedWithoutTheControlCharactersOfItsName()
    {
        var site = new Guid("00000000-0000-0000-0000-000000000001");
        string.CompareOrdinal(site.ToString().ToUpperInvariant(), DefaultSite.Id.ToString().ToUpperInvariant()).ShouldBeLessThan(0);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var cms = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            cms.Set<Site>().Add(new Site { Id = site, Name = "Low\nWarning forged\u001b[31mred\u2028", Slug = $"low-{Guid.NewGuid():N}", DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow });
            await cms.SaveChangesAsync(Token);
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
            database.Set<Asset>().Add(SharedLibraryReportBoundTests.Row(site));
            await database.SaveChangesAsync(Token);
        }

        var item = (await fixture.Services.GetRequiredService<StartupReportFactory>().CreateAsync(Token)).InsecureDefaults.Single(item => item.Id == SharedLibraryReport.HiddenSites);

        item.Message.ShouldContain($"{site:D} (LowWarning forged[31mred)");
        item.Message.ShouldNotContain("\n");
        item.Message.ShouldNotContain("\u001b");
        item.Message.ShouldNotContain("\u2028");
    }
}
