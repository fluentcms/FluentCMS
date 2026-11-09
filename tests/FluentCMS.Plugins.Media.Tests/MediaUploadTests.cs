using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaUploadTests(MediaHost media) : IClassFixture<MediaHost>
{
    [Fact]
    public async Task AJpegAPngAndAPdfBecomeThreeAssetsWhoseBytesTheLocalStorageHolds()
    {
        using var client = await media.AdminAsync();
        var jpeg = Files.Jpeg(640, 480);
        var png = Files.Png(32, 16);
        var pdf = Files.Pdf();
        var files = StoredFiles();

        var items = await UploadedAsync(client, null, ("photo.jpg", jpeg), ("C:\\Users\\me\\icon.png", png), ("report.pdf", pdf));

        items.Count.ShouldBe(3);
        Describe(items[0]!).ShouldBe(("photo.jpg", "image/jpeg", jpeg.LongLength, Sha256(jpeg), 640, 480));
        Describe(items[1]!).ShouldBe(("icon.png", "image/png", png.LongLength, Sha256(png), 32, 16));
        Describe(items[2]!).ShouldBe(("report.pdf", "application/pdf", pdf.LongLength, Sha256(pdf), (int?)null, (int?)null));

        await using var scope = media.Services.CreateAsyncScope();
        var ids = items.Select(item => Id(item!)).ToList();
        var assets = await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking().Where(asset => ids.Contains(asset.Id)).ToListAsync(Token);
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>().ShouldBeOfType<LocalFileStorage>();
        List<string> stored = [];
        foreach (var asset in assets)
        {
            asset.StorageProvider.ShouldBe("local");
            await using var stream = (await storage.OpenReadAsync(asset.StorageKey, Token)).ShouldNotBeNull();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, Token);
            stored.Add(Sha256(copy.ToArray()));
        }

        stored.ShouldBe([Sha256(jpeg), Sha256(png), Sha256(pdf)], ignoreOrder: true);
        assets.ShouldAllBe(asset => asset.StorageKey.StartsWith(asset.SiteId.ToString("N") + "/", StringComparison.Ordinal));
        StoredFiles().Except(files).Count().ShouldBe(3);
    }

    [Fact]
    public async Task TheSizeOfGifAndWebPImagesIsReadFromTheirHeaders()
    {
        using var client = await media.AdminAsync();

        var items = await UploadedAsync(client, null, ("a.gif", Files.Gif(7, 9)), ("b.webp", Files.WebPExtended(300, 200)));

        (items[0]!["width"]!.GetValue<int>(), items[0]!["height"]!.GetValue<int>()).ShouldBe((7, 9));
        (items[1]!["mimeType"]!.GetValue<string>(), items[1]!["width"]!.GetValue<int>(), items[1]!["height"]!.GetValue<int>()).ShouldBe(("image/webp", 300, 200));
    }

    [Fact]
    public async Task BytesOfNoAllowedTypeAreRefusedWhateverTheNameAndNothingIsStored()
    {
        using var client = await media.AdminAsync();
        var total = (await GetAsync(client, Root))["total"]!.GetValue<long>();
        var files = StoredFiles();

        using var executable = await UploadAsync(client, null, ("holiday.jpg", Files.WindowsExecutable()));
        using var shebang = await UploadAsync(client, null, ("run.png", Encoding.ASCII.GetBytes("#!/bin/sh\nrm -rf /\n")));
        using var named = await UploadAsync(client, null, ("setup.exe", Files.Png(1, 1)));
        using var text = await UploadAsync(client, null, ("notes.jpg", Encoding.UTF8.GetBytes("just some text")));
        using var mixed = await UploadAsync(client, null, ("good.png", Files.Png(1, 1)), ("bad.jpg", Files.WindowsExecutable()));

        executable.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await CodeAsync(executable)).ShouldBe(MediaErrorCodes.Executable);
        (await CodeAsync(shebang)).ShouldBe(MediaErrorCodes.Executable);
        (await CodeAsync(named)).ShouldBe(MediaErrorCodes.Executable);
        text.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await CodeAsync(text)).ShouldBe(MediaErrorCodes.TypeNotAllowed);
        (await CodeAsync(mixed)).ShouldBe(MediaErrorCodes.Executable);
        (await GetAsync(client, Root))["total"]!.GetValue<long>().ShouldBe(total);
        StoredFiles().ShouldBe(files, ignoreOrder: true);
    }

    [Fact]
    public async Task AFileOverTheLimitAndATypeOffTheSitesListAreRefused()
    {
        using var client = await media.AdminAsync();
        await using var limits = await media.ConfigureAsync(settings =>
        {
            settings.MaxFileSize = 100;
            settings.AllowedTypes = ["image/png"];
        });

        using var large = await UploadAsync(client, null, ("big.png", [.. Files.Png(1, 1), .. new byte[200]]));
        using var refused = await UploadAsync(client, null, ("doc.pdf", Files.Pdf()));
        using var allowed = await UploadAsync(client, null, ("small.png", Files.Png(1, 1)));

        large.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await CodeAsync(large)).ShouldBe(MediaErrorCodes.TooLarge);
        (await CodeAsync(refused)).ShouldBe(MediaErrorCodes.TypeNotAllowed);
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task TheFilesOfARequestTogetherAreHeldToTheRequestLimit()
    {
        using var client = await media.AdminAsync();
        var png = Files.Png(1, 1);
        await using var limit = await media.ConfigureAsync(settings => settings.MaxRequestSize = png.Length * 2);

        using var refused = await UploadAsync(client, null, ("a.png", png), ("b.png", png), ("c.png", png));
        using var accepted = await UploadAsync(client, null, ("a.png", png), ("b.png", png));

        (await CodeAsync(refused)).ShouldBe(MediaErrorCodes.TooLarge);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AnSvgWithAScriptIsRefusedByDefaultAndSanitisedWhenTheSettingsSaySo()
    {
        using var client = await media.AdminAsync();
        var svg = Files.Svg("""<script>alert(1)</script><rect width="5" height="5" onclick="alert(2)"/><a xlink:href="javascript:alert(3)"><circle r="1"/></a><foreignObject><div xmlns="http://www.w3.org/1999/xhtml">x</div></foreignObject>""");
        var svgs = StoredFiles("*.svg");

        using var refused = await UploadAsync(client, null, ("logo.svg", svg));
        await using var sanitised = await media.ConfigureAsync(settings => settings.Svg = SvgHandling.Sanitize);
        var stored = (await UploadedAsync(client, null, ("logo.svg", svg))).Single()!;

        refused.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await CodeAsync(refused)).ShouldBe(MediaErrorCodes.SvgRefused);
        stored["mimeType"]!.GetValue<string>().ShouldBe("image/svg+xml");
        var file = StoredFiles("*.svg").Except(svgs).ShouldHaveSingleItem();
        var text = await File.ReadAllTextAsync(file, Token);
        text.ShouldContain("<rect");
        text.ShouldContain("<circle");
        text.ShouldNotContain("script", Case.Insensitive);
        text.ShouldNotContain("onclick");
        text.ShouldNotContain("foreignObject");
        stored["size"]!.GetValue<long>().ShouldBe(new FileInfo(file).Length);
        stored["checksum"]!.GetValue<string>().ShouldBe(Sha256(await File.ReadAllBytesAsync(file, Token)));
    }

    [Fact]
    public async Task AnUploadNeedsFilesAndAFolderOfTheSite()
    {
        using var client = await media.AdminAsync();

        using var none = await client.PostAsync(new Uri(Root, UriKind.Relative), new MultipartFormDataContent { { new StringContent("x"), "title" } }, Token);
        using var missing = await UploadAsync(client, Guid.CreateVersion7(), ("a.png", Files.Png(1, 1)));

        none.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(missing)).ShouldBe(MediaErrorCodes.FolderNotFound);
    }

    private static (string, string, long, string, int?, int?) Describe(System.Text.Json.Nodes.JsonNode item) => (
        item["fileName"]!.GetValue<string>(),
        item["mimeType"]!.GetValue<string>(),
        item["size"]!.GetValue<long>(),
        item["checksum"]!.GetValue<string>(),
        item["width"]?.GetValue<int>(),
        item["height"]?.GetValue<int>());

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private List<string> StoredFiles(string pattern = "*") => [.. Directory.EnumerateFiles(media.StorageRoot, pattern, SearchOption.AllDirectories)];
}
