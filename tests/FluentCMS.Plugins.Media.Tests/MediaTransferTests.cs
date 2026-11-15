using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Media;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

// The media of a package imported into the site it was exported from, with lines and files written by the test: each file
// passes the checks of an upload in the site of the import, and an asset takes its type, size and image size from them.
public sealed class MediaTransferTests(MediaHost media) : IClassFixture<MediaHost>
{
    private const string Import = "/api/management/v1/transfer/import";

    [Fact]
    public async Task AFileWhoseLineSaysHtmlIsRefusedAndAnImageTakesTheTypeSizeAndImageSizeOfItsBytes()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var html = Encoding.UTF8.GetBytes("<html><body><script>alert(document.domain)</script></body></html>");
        var png = Files.Png(3, 2);
        var (page, image) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var package = await PackageAsync(
            admin,
            [Line(page, "original.html", "text/html", html), Line(image, "logo.png", "text/html", png)],
            (FileOf(page, ".html"), html),
            (FileOf(image, ".png"), png));

        var dry = Items(await ImportAsync(admin, package, dryRun: true));
        var real = Items(await ImportAsync(admin, package));

        dry.ShouldBe(real);
        real.ShouldBe(new Dictionary<Guid, (string, string?)> { [page] = ("error", MediaErrorCodes.TypeNotAllowed) });
        (await AssetAsync(page)).ShouldBeNull();
        var stored = (await AssetAsync(image)).ShouldNotBeNull();
        (stored.MimeType, stored.Size, stored.Width, stored.Height, stored.Checksum).ShouldBe(("image/png", png.LongLength, 3, 2, Sha256(png)));
        stored.StorageKey.ShouldMatch($@"^{DefaultSite.Id:N}/{image:N}/original-[0-9a-f]{{32}}\.png$");
        var original = (await GetAsync(anonymous, $"/api/delivery/v1/media/{image:D}"))["urls"]!["original"]!.GetValue<string>();
        using var served = await FetchAsync(anonymous, original);
        served.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await served.Content.ReadAsByteArrayAsync(Token)).ShouldBe(png);
    }

    [Fact]
    public async Task AnExecutableADisallowedTypeARefusedSvgAndAFileOverTheLimitAreItemErrorsAsInAnUpload()
    {
        await using var rules = await media.ConfigureAsync(settings =>
        {
            settings.MaxFileSize = 1024;
            settings.AllowedTypes = [.. settings.AllowedTypes.Where(type => type != "application/pdf")];
            settings.Svg = SvgHandling.Refuse;
        });
        using var admin = await media.AdminAsync();
        var executable = Files.WindowsExecutable();
        var pdf = Files.Pdf();
        var svg = Files.Svg("""<rect width="10" height="10"/>""");
        byte[] large = [.. Files.Jpeg(8, 8), .. new byte[2048]];
        var renamed = Files.Png(4, 4);
        Guid[] ids = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        var package = await PackageAsync(
            admin,
            [
                Line(ids[0], "tool.png", "image/png", executable),
                Line(ids[1], "report.pdf", "application/pdf", pdf),
                Line(ids[2], "logo.svg", "image/svg+xml", svg),
                Line(ids[3], "photo.jpg", "image/jpeg", large),
                Line(ids[4], "photo.exe", "image/png", renamed),
            ],
            (FileOf(ids[0], ".png"), executable),
            (FileOf(ids[1], ".pdf"), pdf),
            (FileOf(ids[2], ".svg"), svg),
            (FileOf(ids[3], ".jpg"), large),
            (FileOf(ids[4], ".exe"), renamed));

        var dry = Items(await ImportAsync(admin, package, dryRun: true));
        var real = Items(await ImportAsync(admin, package));

        dry.ShouldBe(real);
        real.ShouldBe(new Dictionary<Guid, (string, string?)>
        {
            [ids[0]] = ("error", MediaErrorCodes.Executable),
            [ids[1]] = ("error", MediaErrorCodes.TypeNotAllowed),
            [ids[2]] = ("error", MediaErrorCodes.SvgRefused),
            [ids[3]] = ("error", MediaErrorCodes.TooLarge),
            [ids[4]] = ("error", MediaErrorCodes.Executable),
        });
        foreach (var id in ids)
        {
            (await AssetAsync(id)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task AnSvgOfAPackageIsSanitisedAsAnUploadIsWhenTheSiteSaysSo()
    {
        await using var rules = await media.ConfigureAsync(settings => settings.Svg = SvgHandling.Sanitize);
        using var admin = await media.AdminAsync();
        var svg = Files.Svg("""<rect width="10" height="10"/><script>alert(1)</script>""");
        var id = Guid.CreateVersion7();
        var package = await PackageAsync(admin, [Line(id, "logo.svg", "image/png", svg)], (FileOf(id, ".svg"), svg));

        Counts(await ImportAsync(admin, package)).ShouldBe((1, 0, 0));

        var stored = (await AssetAsync(id)).ShouldNotBeNull();
        stored.MimeType.ShouldBe("image/svg+xml");
        stored.StorageKey.ShouldMatch($@"^{DefaultSite.Id:N}/{id:N}/original-[0-9a-f]{{32}}\.svg$");
        var bytes = await StoredAsync(stored.StorageKey);
        Encoding.UTF8.GetString(bytes).ShouldNotContain("script");
        (stored.Size, stored.Checksum).ShouldBe((bytes.LongLength, Sha256(bytes)));

        // A sanitised file is sanitised to the same bytes, so a line can take its key as it is.
        var copy = Guid.CreateVersion7();
        Counts(await ImportAsync(admin, await PackageAsync(admin, [Line(copy, "copy.svg", "image/png", bytes, key: stored.StorageKey)]))).ShouldBe((1, 0, 0));
        var copied = (await AssetAsync(copy)).ShouldNotBeNull();
        (copied.MimeType, copied.StorageKey, copied.Checksum).ShouldBe(("image/svg+xml", stored.StorageKey, stored.Checksum));
    }

    [Fact]
    public async Task ALineWithoutAFileKeepsTheTypeOfTheFileAtTheKeyItTakes()
    {
        using var admin = await media.AdminAsync();
        var png = Files.Png(6, 5);
        var uploaded = await UploadAsync(admin, png, "photo.png");
        var key = (await AssetAsync(uploaded.Id)).ShouldNotBeNull().StorageKey;
        var (sharing, wrong) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var later = media.Host.Time.GetUtcNow().AddMinutes(5);
        var package = await PackageAsync(
            admin,
            [
                Line(uploaded.Id, "photo.png", "text/html", png, updatedAt: later, size: 1, title: "kept", withFile: false),
                Line(sharing, "shared.png", "text/html", png, key: key),
                Line(wrong, "wrong.png", "image/png", Files.Png(1, 1), key: key),
            ]);

        var dryReport = await ImportAsync(admin, package, dryRun: true);
        var realReport = await ImportAsync(admin, package);
        var (dry, real) = (Items(dryReport), Items(realReport));

        dry.ShouldBe(real);
        Counts(realReport).ShouldBe((1, 1, 1));
        real.ShouldBe(new Dictionary<Guid, (string, string?)> { [wrong] = ("error", MediaErrorCodes.TransferFileMissing) });
        var own = (await AssetAsync(uploaded.Id)).ShouldNotBeNull();
        (own.Title, own.MimeType, own.Size, own.Width, own.Height, own.StorageKey).ShouldBe(("kept", "image/png", png.LongLength, 6, 5, key));
        var shared = (await AssetAsync(sharing)).ShouldNotBeNull();
        (shared.MimeType, shared.Size, shared.Width, shared.Height, shared.StorageKey).ShouldBe(("image/png", png.LongLength, 6, 5, key));
        (await AssetAsync(wrong)).ShouldBeNull();
    }

    [Fact]
    public async Task AFileOfThePackageOrAKeyOfTheStorageIsTakenByOneLineOfAnImport()
    {
        using var admin = await media.AdminAsync();
        var png = Files.Png(2, 2);
        var key = (await AssetAsync((await UploadAsync(admin, png, "photo.png")).Id)).ShouldNotBeNull().StorageKey;
        Guid[] ids = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        var again = Line(ids[1], "again.png", "image/png", png);
        again["file"] = FileOf(ids[0], ".png");
        var package = await PackageAsync(
            admin,
            [Line(ids[0], "first.png", "image/png", png), again, Line(ids[2], "keyed.png", "image/png", png, key: key), Line(ids[3], "keyed-again.png", "image/png", png, key: key)],
            (FileOf(ids[0], ".png"), png));

        var dryReport = await ImportAsync(admin, package, dryRun: true);
        var realReport = await ImportAsync(admin, package);
        var (dry, real) = (Items(dryReport), Items(realReport));

        dry.ShouldBe(real);
        Counts(realReport).ShouldBe((2, 0, 2));
        real.ShouldBe(new Dictionary<Guid, (string, string?)>
        {
            [ids[1]] = ("error", MediaErrorCodes.TransferFileMissing),
            [ids[3]] = ("error", MediaErrorCodes.TransferFileMissing),
        });
        (await AssetAsync(ids[0])).ShouldNotBeNull();
        (await AssetAsync(ids[1])).ShouldBeNull();
        (await AssetAsync(ids[2])).ShouldNotBeNull().StorageKey.ShouldBe(key);
        (await AssetAsync(ids[3])).ShouldBeNull();
    }

    // Windows and macOS read the three spellings of a key as one file, so only the first is read.
    [Fact]
    public async Task AKeySpelledWithATrailingDotOrInAnotherCaseIsNotReadAgain()
    {
        using var admin = await media.AdminAsync();
        var png = Files.Png(3, 3);
        var key = (await AssetAsync((await UploadAsync(admin, png, "photo.png")).Id)).ShouldNotBeNull().StorageKey;
        key.ShouldEndWith("/original.png");
        Guid[] ids = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        var package = await PackageAsync(
            admin,
            [
                Line(ids[0], "first.png", "image/png", png, key: key),
                Line(ids[1], "dotted.png", "image/png", png, key: key + "."),
                Line(ids[2], "upper.png", "image/png", png, key: key[..^"original.png".Length] + "ORIGINAL.png"),
            ]);

        var dryReport = await ImportAsync(admin, package, dryRun: true);
        var realReport = await ImportAsync(admin, package);

        Items(dryReport).ShouldBe(Items(realReport));
        Counts(realReport).ShouldBe((1, 0, 2));
        var messages = realReport["items"]!.AsArray().ToDictionary(item => Guid.Parse(item!["id"]!.GetValue<string>()), item => (item!["code"]!.GetValue<string>(), item["message"]!.GetValue<string>()));
        messages[ids[1]].Item1.ShouldBe(MediaErrorCodes.TransferFileMissing);
        messages[ids[1]].Item2.ShouldContain("ends with a dot");
        messages[ids[2]].Item1.ShouldBe(MediaErrorCodes.TransferFileMissing);
        messages[ids[2]].Item2.ShouldContain("earlier line");
        (await AssetAsync(ids[0])).ShouldNotBeNull();
        (await AssetAsync(ids[1])).ShouldBeNull();
        (await AssetAsync(ids[2])).ShouldBeNull();
    }

    // The asset keeps the file it has, so a site whose rules no longer allow the file does not refuse the line, and the
    // file of the package is not read.
    [Fact]
    public async Task ALineWithTheChecksumOfTheAssetKeepsItsFileWhateverTheRulesOfTheSiteSayNow()
    {
        using var admin = await media.AdminAsync();
        var png = Files.Png(4, 3);
        var uploaded = await UploadAsync(admin, png, "photo.png");
        var key = (await AssetAsync(uploaded.Id)).ShouldNotBeNull().StorageKey;
        await using var rules = await media.ConfigureAsync(settings => settings.AllowedTypes = [.. settings.AllowedTypes.Where(type => type != "image/png")]);
        var later = media.Host.Time.GetUtcNow().AddMinutes(5);
        var package = await PackageAsync(admin, [Line(uploaded.Id, "photo.png", "image/png", png, updatedAt: later, title: "retitled")], (FileOf(uploaded.Id, ".png"), png));

        var dry = await ImportAsync(admin, package, dryRun: true);
        var real = await ImportAsync(admin, package);

        Items(dry).ShouldBeEmpty();
        Items(real).ShouldBeEmpty();
        Counts(real).ShouldBe((0, 1, 0));
        var stored = (await AssetAsync(uploaded.Id)).ShouldNotBeNull();
        (stored.Title, stored.StorageKey, stored.MimeType).ShouldBe(("retitled", key, "image/png"));
    }

    // The checks of an upload find no duration, so a file the import stores has none, as after a replace; a file the asset
    // keeps keeps the duration the instance has, never the line's.
    [Fact]
    public async Task AFileTheImportStoresHasNoDurationAndAKeptFileKeepsTheInstances()
    {
        using var admin = await media.AdminAsync();
        var (kept, replaced) = (Files.Png(5, 4), Files.Png(5, 5));
        var (keeping, replacing, created) = ((await UploadAsync(admin, kept, "kept.png")).Id, (await UploadAsync(admin, Files.Png(6, 6), "old.png")).Id, Guid.CreateVersion7());
        await SetDurationAsync(keeping, 12.5);
        await SetDurationAsync(replacing, 3);
        var later = media.Host.Time.GetUtcNow().AddMinutes(5);
        List<Dictionary<string, object?>> lines =
        [
            Line(keeping, "kept.png", "image/png", kept, updatedAt: later, withFile: false),
            Line(replacing, "new.png", "image/png", replaced, updatedAt: later),
            Line(created, "created.png", "image/png", Files.Png(7, 7)),
        ];
        foreach (var line in lines)
        {
            line["duration"] = 99.0;
        }

        var report = await ImportAsync(admin, await PackageAsync(admin, lines, (FileOf(replacing, ".png"), replaced), (FileOf(created, ".png"), Files.Png(7, 7))));

        Items(report).ShouldBeEmpty();
        (await AssetAsync(keeping)).ShouldNotBeNull().Duration.ShouldBe(12.5);
        (await AssetAsync(replacing)).ShouldNotBeNull().Duration.ShouldBeNull();
        (await AssetAsync(created)).ShouldNotBeNull().Duration.ShouldBeNull();
    }

    // The import writes the settings of the package first, so the media section checks the files against them, while a dry
    // run, which writes no settings, checks against those the site has.
    [Fact]
    public async Task TheMediaSettingsOfThePackageAreWrittenBeforeTheMediaSectionReadsThem()
    {
        await using var restore = await media.ConfigureAsync(_ => { });
        using var admin = await media.AdminAsync();
        byte[] large = [.. Files.Jpeg(8, 8), .. new byte[2048]];
        var id = Guid.CreateVersion7();
        byte[] package;
        await using (await media.ConfigureAsync(settings => settings.MaxFileSize = 1024))
        {
            package = await PackageAsync(admin, [Line(id, "photo.jpg", "image/jpeg", large)], withSettings: true, (FileOf(id, ".jpg"), large));
        }

        (await SettingsAsync()).MaxFileSize.ShouldBeGreaterThan(large.Length);

        var dry = await ImportAsync(admin, package, dryRun: true);
        var real = await ImportAsync(admin, package);

        Items(dry).ShouldBeEmpty();
        Counts(dry).ShouldBe((1, 0, 0));
        Items(real).ShouldBe(new Dictionary<Guid, (string, string?)> { [id] = ("error", MediaErrorCodes.TooLarge) });
        (await AssetAsync(id)).ShouldBeNull();
        (await SettingsAsync()).MaxFileSize.ShouldBe(1024);
    }

    // As a move through the service: never into itself or a folder below it, with a name it takes, and no deeper than folders
    // nest, in a dry run as in the import.
    [Fact]
    public async Task AFolderLineIsHeldToWhatACreateAndAMoveAllow()
    {
        using var admin = await media.AdminAsync();
        var (top, below, other, otherChild) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var first = await PackageAsync(admin, [], [FolderLine(top, null, Unique("top")), FolderLine(below, top, "below"), FolderLine(other, null, Unique("other")), FolderLine(otherChild, other, "child")]);
        Counts(await ImportAsync(admin, first), MediaTransfer.FoldersSection).ShouldBe((4, 0, 0));
        var (self, named) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var chain = Enumerable.Range(0, MediaModel.MaxDepth + 1).Select(_ => Guid.CreateVersion7()).ToList();
        List<Dictionary<string, object?>> lines =
        [
            FolderLine(top, below, "moved"),
            FolderLine(self, self, Unique("self")),
            FolderLine(named, null, "a/b"),
            FolderLine(Guid.Empty, null, Unique("empty")),
            .. chain.Select((id, index) => FolderLine(id, index == 0 ? null : chain[index - 1], index == 0 ? Unique("level") : "level")),
            FolderLine(other, chain[MediaModel.MaxDepth - 2], "deep"),
        ];
        var package = await PackageAsync(admin, [], lines);

        var dryReport = await ImportAsync(admin, package, dryRun: true);
        var realReport = await ImportAsync(admin, package);

        Items(dryReport, MediaTransfer.FoldersSection).ShouldBe(Items(realReport, MediaTransfer.FoldersSection));
        Counts(realReport, MediaTransfer.FoldersSection).ShouldBe((MediaModel.MaxDepth, 0, 6));
        Items(realReport, MediaTransfer.FoldersSection).ShouldBe(new Dictionary<Guid, (string, string?)>
        {
            [top] = ("error", MediaErrorCodes.FolderCycle),
            [self] = ("error", MediaErrorCodes.FolderCycle),
            [named] = ("error", MediaErrorCodes.InvalidField),
            [Guid.Empty] = ("error", MediaErrorCodes.InvalidField),
            [chain[^1]] = ("error", MediaErrorCodes.InvalidField),
            [other] = ("error", MediaErrorCodes.InvalidField),
        });
        (await FolderAsync(top)).ShouldNotBeNull().ParentId.ShouldBeNull();
        (await FolderAsync(other)).ShouldNotBeNull().ParentId.ShouldBeNull();
        (await FolderAsync(self)).ShouldBeNull();
        (await FolderAsync(chain[^1])).ShouldBeNull();
    }

    // The database never refuses a line whose fields pass, so a dry run reports what the import does.
    [Fact]
    public async Task TheFieldsOfAnAssetLineAreHeldToWhatTheServiceAllows()
    {
        using var admin = await media.AdminAsync();
        Guid[] ids = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.Empty];
        var pngs = ids.Select((_, index) => Files.Png(10 + index, 3)).ToArray();
        var lines = ids.Select((id, index) => Line(id, $"photo-{index}.png", "image/png", pngs[index])).ToList();
        lines[0]["title"] = new string('t', MediaModel.TitleMaxLength + 1);
        lines[1]["alt"] = new Dictionary<string, object?> { ["en"] = 5 };
        lines[2]["tags"] = new[] { "fine", "with \"quote\"" };
        lines[3]["focalX"] = 2.0;
        lines[3]["focalY"] = 0.5;
        lines[4]["tags"] = new[] { " spaced ", "net", "spaced" };
        lines[4]["alt"] = new Dictionary<string, object?> { ["en"] = "An image", ["fr"] = "Une image" };
        var package = await PackageAsync(admin, lines, [.. ids.Select((id, index) => (FileOf(id, ".png"), pngs[index]))]);

        var dryReport = await ImportAsync(admin, package, dryRun: true);
        var realReport = await ImportAsync(admin, package);

        Items(dryReport).ShouldBe(Items(realReport));
        Counts(realReport).ShouldBe((1, 0, 5));
        Items(realReport).ShouldBe(new Dictionary<Guid, (string, string?)>
        {
            [ids[0]] = ("error", MediaErrorCodes.InvalidField),
            [ids[1]] = ("error", MediaErrorCodes.InvalidField),
            [ids[2]] = ("error", MediaErrorCodes.InvalidField),
            [ids[3]] = ("error", MediaErrorCodes.InvalidField),
            [Guid.Empty] = ("error", MediaErrorCodes.InvalidField),
        });
        var stored = (await AssetAsync(ids[4])).ShouldNotBeNull();
        stored.Tags.ShouldBe("""["spaced","net"]""");
        MediaJson.Map(stored.Alt)["FR"].ShouldBe("Une image");
    }

    // A file the import wrote for a row the database does not save is deleted again, and the report says no more of the
    // failure than its kind and the step that failed, since the exception can name the server's paths. A save that fails is
    // FCMS3221 whatever the exception, and a write of the storage FCMS3220. The file a row names before an update whose save
    // fails stays.
    [Fact]
    public async Task AFileWhoseRowIsNotSavedIsDeletedAndTheReportNamesNoDetailOfTheServer()
    {
        var (refused, failed, blocked) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var save = new FailingSave();
        save.Refused.Add(refused);
        save.Failed.Add(failed);
        await using var host = new MediaHost();
        host.Host.Configure(cms => cms.Services.ConfigureDbContext<PluginDbContext<MediaPlugin>>(options => options.AddInterceptors(save)));
        using var admin = await host.AdminAsync();
        var replaced = await UploadAsync(admin, Files.Png(4, 4), "kept.png");
        var key = (await AssetAsync(host, replaced.Id)).ShouldNotBeNull().StorageKey;
        save.Refused.Add(replaced.Id);

        // A file where the storage needs the folder of the asset, so that its write fails with the path in its message.
        var library = Path.Combine(host.StorageRoot, DefaultSite.Id.ToString("N"));
        Directory.CreateDirectory(library);
        await File.WriteAllBytesAsync(Path.Combine(library, blocked.ToString("N")), [1], Token);
        var (one, two, three, four) = (Files.Png(1, 2), Files.Png(2, 1), Files.Png(3, 1), Files.Png(1, 3));
        var package = await PackageAsync(
            admin,
            [
                Line(refused, "one.png", "image/png", one),
                Line(failed, "two.png", "image/png", two),
                Line(blocked, "three.png", "image/png", three),
                Line(replaced.Id, "four.png", "image/png", four, updatedAt: host.Host.Time.GetUtcNow().AddMinutes(5)),
            ],
            (FileOf(refused, ".png"), one),
            (FileOf(failed, ".png"), two),
            (FileOf(blocked, ".png"), three),
            (FileOf(replaced.Id, ".png"), four));

        var report = await ImportAsync(admin, package);

        Items(report).ShouldBe(new Dictionary<Guid, (string, string?)>
        {
            [refused] = ("error", MediaErrorCodes.TransferIdTaken),
            [failed] = ("error", MediaErrorCodes.TransferIdTaken),
            [blocked] = ("error", MediaErrorCodes.TransferFileMissing),
            [replaced.Id] = ("error", MediaErrorCodes.TransferIdTaken),
        });
        var items = report["items"]!.ToJsonString();
        items.ShouldNotContain(FailingSave.Detail);
        items.ShouldNotContain(DefaultSite.Id.ToString("N"));
        var files = Directory.EnumerateFiles(host.StorageRoot, "*", SearchOption.AllDirectories).ToList();
        files.ShouldNotContain(path => path.Contains(refused.ToString("N"), StringComparison.Ordinal) || path.Contains(failed.ToString("N"), StringComparison.Ordinal));
        (await AssetAsync(host, replaced.Id)).ShouldNotBeNull().StorageKey.ShouldBe(key);
        files.Where(path => path.Contains(replaced.Id.ToString("N"), StringComparison.Ordinal)).Select(path => Path.GetRelativePath(host.StorageRoot, path).Replace('\\', '/')).ShouldBe([key]);
    }

    // A read of the database that fails is one of the database, as a failed save is, and not one of the storage.
    [Fact]
    public async Task AReadOfTheDatabaseThatFailsIsReportedAsOneOfTheDatabaseWithNoDetailOfTheServer()
    {
        var (folder, asset) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var read = new FailingRead(folder, asset);
        await using var host = new MediaHost();
        host.Host.Configure(cms => cms.Services.ConfigureDbContext<PluginDbContext<MediaPlugin>>(options => options.AddInterceptors(read)));
        using var admin = await host.AdminAsync();
        var png = Files.Png(2, 3);
        var package = await PackageAsync(admin, [Line(asset, "read.png", "image/png", png)], [FolderLine(folder, null, Unique("read"))], withSettings: false, [(FileOf(asset, ".png"), png)]);

        var report = await ImportAsync(admin, package);
        read.Armed = false;

        Items(report, MediaTransfer.FoldersSection).ShouldBe(new Dictionary<Guid, (string, string?)> { [folder] = ("error", MediaErrorCodes.TransferIdTaken) });
        Items(report).ShouldBe(new Dictionary<Guid, (string, string?)> { [asset] = ("error", MediaErrorCodes.TransferIdTaken) });
        report["items"]!.ToJsonString().ShouldNotContain(FailingRead.Detail);
        (await AssetAsync(host, asset)).ShouldBeNull();
    }

    // With Shared, imports into two sites write one library while each holds the lock of its own site, so both can find no
    // row of an asset and write its file. The one whose save then fails deletes only the file it wrote, under a key of its
    // own, and the file the other's row names stays.
    [Fact]
    public async Task AFailedSaveLeavesTheFileThatAnImportIntoAnotherSiteOfTheLibraryWroteForTheSameAsset()
    {
        var id = Guid.CreateVersion7();
        var race = new RacingSave(id);
        await using var host = new MediaHost();
        host.Host.Configure(cms => cms.Services.ConfigureDbContext<PluginDbContext<MediaPlugin>>(options => options.AddInterceptors(race)));
        using var admin = await host.AdminAsync();
        using var other = await MediaLibraryTesting.InSiteAsync(host, await MediaLibraryTesting.CreateSiteAsync(host));
        var png = Files.Png(3, 4);
        var package = await PackageAsync(admin, [Line(id, "raced.png", "image/png", png)], (FileOf(id, ".png"), png));
        JsonNode? first = null;
        race.Before = async () => first = await ImportAsync(other, package);

        var second = await ImportAsync(admin, package);

        Counts(first.ShouldNotBeNull()).ShouldBe((1, 0, 0));
        Items(second).ShouldBe(new Dictionary<Guid, (string, string?)> { [id] = ("error", MediaErrorCodes.TransferIdTaken) });
        var stored = (await AssetAsync(host, id)).ShouldNotBeNull();
        stored.SiteId.ShouldBe(DefaultSite.Id);
        (await File.ReadAllBytesAsync(Path.Combine(host.StorageRoot, stored.StorageKey), Token)).ShouldBe(png);
        Directory.EnumerateFiles(host.StorageRoot, "*", SearchOption.AllDirectories).Count(path => path.Contains(id.ToString("N"), StringComparison.Ordinal)).ShouldBe(1);
    }

    // Core reads no path out of the folder of the contributor, so such a line has no file, even when the package has a file
    // of that very name, which would pass every other check.
    [Theory]
    [InlineData("../../manifest.json")]
    [InlineData("files/../../schema.json")]
    [InlineData("/manifest.json")]
    public async Task AFilePathOutOfTheFolderOfTheContributorIsNotRead(string path)
    {
        using var admin = await media.AdminAsync();
        var id = Guid.CreateVersion7();
        var png = Files.Png(2, 2);
        var line = Line(id, "photo.png", "image/png", png);
        line["file"] = path;
        var package = await PackageAsync(admin, [line], (path, png));
        using (var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            archive.GetEntry("plugins/media/" + path).ShouldNotBeNull();
        }

        var dry = await ImportAsync(admin, package, dryRun: true);
        var real = await ImportAsync(admin, package);

        Items(dry).ShouldBe(Items(real));
        Items(real).ShouldBe(new Dictionary<Guid, (string, string?)> { [id] = ("error", MediaErrorCodes.TransferFileMissing) });
        (await AssetAsync(id)).ShouldBeNull();
    }

    // A file being checked is kept in the shared temporary folder, so on Unix only its owner reads it.
    [Fact]
    public void AMediaFileBeingCheckedIsTheOwnersOnly()
    {
        string path;
        using (var file = MediaService.TemporaryFile())
        {
            path = file.Name;
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        File.Exists(path).ShouldBeFalse();
    }

    private static Task<byte[]> PackageAsync(HttpClient admin, IEnumerable<Dictionary<string, object?>> lines, params (string Path, byte[] Bytes)[] files) =>
        PackageAsync(admin, lines, [], withSettings: false, files);

    private static Task<byte[]> PackageAsync(HttpClient admin, IEnumerable<Dictionary<string, object?>> lines, bool withSettings, params (string Path, byte[] Bytes)[] files) =>
        PackageAsync(admin, lines, [], withSettings, files);

    private static Task<byte[]> PackageAsync(HttpClient admin, IEnumerable<Dictionary<string, object?>> lines, IEnumerable<Dictionary<string, object?>> folders) =>
        PackageAsync(admin, lines, folders, withSettings: false, []);

    // A package of the default site, exported by the host, whose media folder holds the lines and files given. It carries
    // the settings of the site only when asked, so that the import otherwise keeps the media settings of the test.
    private static async Task<byte[]> PackageAsync(HttpClient admin, IEnumerable<Dictionary<string, object?>> lines, IEnumerable<Dictionary<string, object?>> folders, bool withSettings, (string Path, byte[] Bytes)[] files)
    {
        using var exported = await admin.PostAsync(new Uri("/api/management/v1/transfer/export", UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Token);
        exported.StatusCode.ShouldBe(HttpStatusCode.OK, await exported.Content.ReadAsStringAsync(Token));
        using var source = new ZipArchive(new MemoryStream(await exported.Content.ReadAsByteArrayAsync(Token)), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries.Where(entry => (withSettings || entry.FullName is not "settings.json") && entry.FullName is not "roles.json" && !entry.FullName.StartsWith("plugins/media/", StringComparison.Ordinal)))
            {
                await using var from = entry.Open();
                await using var to = package.CreateEntry(entry.FullName).Open();
                await from.CopyToAsync(to, Token);
            }

            Write(package, "plugins/media/folders.jsonl", Encoding.UTF8.GetBytes(string.Join('\n', folders.Select(line => JsonSerializer.Serialize(line, JsonSerializerOptions.Web)))));
            Write(package, "plugins/media/assets.jsonl", Encoding.UTF8.GetBytes(string.Join('\n', lines.Select(line => JsonSerializer.Serialize(line, JsonSerializerOptions.Web)))));
            foreach (var (path, bytes) in files)
            {
                Write(package, "plugins/media/" + path, bytes);
            }
        }

        return output.ToArray();
    }

    private static Dictionary<string, object?> FolderLine(Guid id, Guid? parentId, string name) => new() { ["id"] = id, ["parentId"] = parentId, ["name"] = name };

    private static void Write(ZipArchive package, string path, byte[] bytes)
    {
        using var stream = package.CreateEntry(path).Open();
        stream.Write(bytes);
    }

    // A line as the export writes it, of the bytes given, with a file of the package unless it names a key of the storage or
    // has no file at all.
    private static Dictionary<string, object?> Line(Guid id, string fileName, string mimeType, byte[] bytes, DateTimeOffset? updatedAt = null, long size = 1, string? title = null, string? key = null, bool withFile = true)
    {
        var at = updatedAt ?? DateTimeOffset.UnixEpoch;
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["fileName"] = fileName,
            ["mimeType"] = mimeType,
            ["size"] = size,
            ["checksum"] = Sha256(bytes),
            ["width"] = 1,
            ["height"] = 1,
            ["title"] = title,
            ["isPrivate"] = false,
            ["createdAt"] = at,
            ["createdBy"] = Guid.Empty,
            ["updatedAt"] = at,
            ["updatedBy"] = Guid.Empty,
            ["storageProvider"] = key is null ? null : "local",
            ["storageKey"] = key,
            ["file"] = key is null && withFile ? FileOf(id, Path.GetExtension(fileName)) : null,
        };
    }

    private static string FileOf(Guid id, string extension) => $"files/{id:N}/original{extension}";

    private static async Task<JsonNode> ImportAsync(HttpClient admin, byte[] package, bool dryRun = false)
    {
        using var body = new ByteArrayContent(package);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var response = await admin.PostAsync(new Uri(Import + (dryRun ? "?dryRun=true" : ""), UriKind.Relative), body, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonNode.Parse(text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1])!["report"]!;
    }

    private static (int Created, int Updated, int Errors) Counts(JsonNode report, string section = MediaTransfer.AssetsSection) =>
        report["sections"]![section] is { } counts
            ? (counts["created"]!.GetValue<int>(), counts["updated"]!.GetValue<int>(), counts["errors"]!.GetValue<int>())
            : (0, 0, 0);

    // The conflicts and errors of the section, the assets unless named, which are what a report lists.
    private static Dictionary<Guid, (string Outcome, string? Code)> Items(JsonNode report, string section = MediaTransfer.AssetsSection) =>
        report["items"]!.AsArray()
            .Where(item => item!["section"]!.GetValue<string>() == section)
            .ToDictionary(item => Guid.Parse(item!["id"]!.GetValue<string>()), item => (item!["outcome"]!.GetValue<string>(), item["code"]?.GetValue<string>()));

    private Task<Asset?> AssetAsync(Guid id) => AssetAsync(media, id);

    private static async Task<Asset?> AssetAsync(MediaHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking().FirstOrDefaultAsync(asset => asset.Id == id, Token);
    }

    private async Task<Folder?> FolderAsync(Guid id)
    {
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Folder>().AsNoTracking().FirstOrDefaultAsync(folder => folder.Id == id, Token);
    }

    private async Task SetDurationAsync(Guid asset, double duration)
    {
        await using var scope = media.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().Where(item => item.Id == asset).ExecuteUpdateAsync(set => set.SetProperty(item => item.Duration, duration), Token);
    }

    private async Task<MediaSettings> SettingsAsync()
    {
        await using var scope = media.Services.CreateAsyncScope();
        using (SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id)))
        {
            return await scope.ServiceProvider.GetRequiredService<ISettings<MediaSettings>>().GetAsync(new SiteId(DefaultSite.Id), Token);
        }
    }

    // A save of the media rows that the database refuses for some assets and that fails otherwise for others, each with a
    // detail of the server that no report may carry.
    private sealed class FailingSave : SaveChangesInterceptor
    {
        public const string Detail = "/srv/fluentcms/secret";

        public HashSet<Guid> Refused { get; } = [];

        public HashSet<Guid> Failed { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var saved = eventData.Context!.ChangeTracker.Entries<Asset>().Select(entry => entry.Entity.Id).ToList();
            return saved.Any(Refused.Contains) ? throw new DbUpdateException($"The database refused the row at {Detail}.")
                : saved.Any(Failed.Contains) ? throw new InvalidOperationException($"The server failed at {Detail}.")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    // A query of the media rows that fails, with a detail of the server, whenever it names one of the ids given.
    private sealed class FailingRead(params Guid[] ids) : DbCommandInterceptor
    {
        public const string Detail = "/srv/fluentcms/read-secret";

        // Off once the import is done, so that the test reads the rows itself.
        public bool Armed { get; set; } = true;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            Armed && command.Parameters.Cast<DbParameter>().Any(parameter => Names(parameter.Value))
                ? throw new InvalidOperationException($"The database failed at {Detail}.")
                : base.ReaderExecutingAsync(command, eventData, result, cancellationToken);

        private bool Names(object? value) => value switch
        {
            Guid id => ids.Contains(id),
            string text => Guid.TryParse(text, out var id) && ids.Contains(id),
            byte[] { Length: 16 } bytes => ids.Contains(new Guid(bytes)),
            _ => false,
        };
    }

    // The first save that adds the asset runs the other import before it goes on, as when two imports find no row of the
    // asset and the other saves first.
    private sealed class RacingSave(Guid asset) : SaveChangesInterceptor
    {
        private int _armed = 1;

        public Func<Task>? Before { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Before is { } before
                && eventData.Context!.ChangeTracker.Entries<Asset>().Any(entry => entry.State == EntityState.Added && entry.Entity.Id == asset)
                && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                await before();
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private async Task<byte[]> StoredAsync(string key)
    {
        await using var scope = media.Services.CreateAsyncScope();
        await using var stream = (await scope.ServiceProvider.GetRequiredService<IFileStorage>().OpenReadAsync(key, Token)).ShouldNotBeNull();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Token);
        return copy.ToArray();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
