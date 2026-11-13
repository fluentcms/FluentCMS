using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
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
        real[page].ShouldBe(("error", MediaErrorCodes.TypeNotAllowed));
        real[image].ShouldBe(("created", null));
        (await AssetAsync(page)).ShouldBeNull();
        var stored = (await AssetAsync(image)).ShouldNotBeNull();
        (stored.MimeType, stored.Size, stored.Width, stored.Height, stored.Checksum).ShouldBe(("image/png", png.LongLength, 3, 2, Sha256(png)));
        stored.StorageKey.ShouldEndWith("/original.png");
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

        Items(await ImportAsync(admin, package))[id].ShouldBe(("created", null));

        var stored = (await AssetAsync(id)).ShouldNotBeNull();
        stored.MimeType.ShouldBe("image/svg+xml");
        stored.StorageKey.ShouldEndWith("/original.svg");
        var bytes = await StoredAsync(stored.StorageKey);
        Encoding.UTF8.GetString(bytes).ShouldNotContain("script");
        (stored.Size, stored.Checksum).ShouldBe((bytes.LongLength, Sha256(bytes)));

        // A sanitised file is sanitised to the same bytes, so a line can take its key as it is.
        var copy = Guid.CreateVersion7();
        Items(await ImportAsync(admin, await PackageAsync(admin, [Line(copy, "copy.svg", "image/png", bytes, key: stored.StorageKey)])))[copy].ShouldBe(("created", null));
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

        var dry = Items(await ImportAsync(admin, package, dryRun: true));
        var real = Items(await ImportAsync(admin, package));

        dry.ShouldBe(real);
        real[uploaded.Id].ShouldBe(("updated", null));
        real[sharing].ShouldBe(("created", null));
        real[wrong].ShouldBe(("error", MediaErrorCodes.TransferFileMissing));
        var own = (await AssetAsync(uploaded.Id)).ShouldNotBeNull();
        (own.Title, own.MimeType, own.Size, own.Width, own.Height, own.StorageKey).ShouldBe(("kept", "image/png", png.LongLength, 6, 5, key));
        var shared = (await AssetAsync(sharing)).ShouldNotBeNull();
        (shared.MimeType, shared.Size, shared.Width, shared.Height, shared.StorageKey).ShouldBe(("image/png", png.LongLength, 6, 5, key));
        (await AssetAsync(wrong)).ShouldBeNull();
    }

    // A package of the default site, exported by the host, whose media folder holds the lines and files given. It carries no
    // settings, so that the import keeps the media settings of the test.
    private static async Task<byte[]> PackageAsync(HttpClient admin, IEnumerable<Dictionary<string, object?>> lines, params (string Path, byte[] Bytes)[] files)
    {
        using var exported = await admin.PostAsync(new Uri("/api/management/v1/transfer/export", UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Token);
        exported.StatusCode.ShouldBe(HttpStatusCode.OK, await exported.Content.ReadAsStringAsync(Token));
        using var source = new ZipArchive(new MemoryStream(await exported.Content.ReadAsByteArrayAsync(Token)), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries.Where(entry => entry.FullName is not "settings.json" and not "roles.json" && !entry.FullName.StartsWith("plugins/media/", StringComparison.Ordinal)))
            {
                await using var from = entry.Open();
                await using var to = package.CreateEntry(entry.FullName).Open();
                await from.CopyToAsync(to, Token);
            }

            Write(package, "plugins/media/assets.jsonl", Encoding.UTF8.GetBytes(string.Join('\n', lines.Select(line => JsonSerializer.Serialize(line, JsonSerializerOptions.Web)))));
            foreach (var (path, bytes) in files)
            {
                Write(package, "plugins/media/" + path, bytes);
            }
        }

        return output.ToArray();
    }

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

    private static Dictionary<Guid, (string Outcome, string? Code)> Items(JsonNode report) =>
        report["items"]!.AsArray()
            .Where(item => item!["section"]!.GetValue<string>() == MediaTransfer.AssetsSection)
            .ToDictionary(item => Guid.Parse(item!["id"]!.GetValue<string>()), item => (item!["outcome"]!.GetValue<string>(), item["code"]?.GetValue<string>()));

    private async Task<Asset?> AssetAsync(Guid id)
    {
        await using var scope = media.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking().FirstOrDefaultAsync(asset => asset.Id == id, Token);
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
