using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SkiaSharp;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaReplaceTests(PostHost media) : IClassFixture<PostHost>
{
    [Fact]
    public async Task AReplaceKeepsTheIdMetadataAndReferencesAndTheNextTransformServesTheNewFile()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(40, 20), "bands.png");
        (await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset.Id:D}", UriKind.Relative), new { alt = "Four bands" }, Token)).Dispose();
        var post = await EntryAsync(admin, "post", "Harbour");
        await media.ReferAsync("post", post, "hero", asset.Id);
        var url = await UrlAsync(media, asset, new MediaImageOptions { Width = 20 });
        using (var before = await FetchAsync(anonymous, url))
        {
            using var image = Decode((await ImageAsync(before)).Bytes);
            image.GetPixel(1, 5).ShouldBe(Red);
        }

        var stale = await FilesOfAsync(media, asset);

        using var replace = await ReplaceAsync(admin, asset.Id, ("blue.png", Solid(30, 30, Blue)));
        var replaced = await JsonAsync(replace);
        using var after = await FetchAsync(anonymous, url);
        using var served = Decode((await ImageAsync(after)).Bytes);
        await media.DrainOutboxAsync();
        var activity = (await GetAsync(admin, $"/api/management/v1/activity?targetType=media&targetId={asset.Id:D}&action=media.replaced"))["items"]!.AsArray();

        Id(replaced).ShouldBe(asset.Id);
        replaced["fileName"]!.GetValue<string>().ShouldBe("blue.png");
        (replaced["width"]!.GetValue<int>(), replaced["height"]!.GetValue<int>()).ShouldBe((30, 30));
        replaced["alt"]!.GetValue<string>().ShouldBe("Four bands");
        replaced["checksum"]!.GetValue<string>().ShouldNotBe(asset.Checksum);
        (served.Width, served.Height).ShouldBe((20, 20));
        served.GetPixel(1, 5).ShouldBe(Blue);
        (await media.ReferencesToAsync(asset.Id)).ShouldBe(1);
        stale.Count.ShouldBe(2);
        stale.ShouldAllBe(file => !File.Exists(file));
        (await FilesOfAsync(media, asset)).Count.ShouldBe(2);
        activity.Single()!["targetId"]!.GetValue<string>().ShouldBe(asset.Id.ToString("D"));
        activity.Single()!["details"]!["mimeType"]!.GetValue<string>().ShouldBe("image/png");
    }

    [Fact]
    public async Task AReplaceRunsTheChecksOfAnUploadAndLeavesTheAssetAsItWas()
    {
        using var admin = await media.AdminAsync();
        var asset = await UploadAsync(admin, Bands(8, 8), "bands.png");
        using var reader = await media.ClientWithAsync(admin, new { permission = "media.read" });

        using var executable = await ReplaceAsync(admin, asset.Id, ("photo.png", Files.WindowsExecutable()));
        using var two = await ReplaceAsync(admin, asset.Id, ("a.png", Files.Png(1, 1)), ("b.png", Files.Png(1, 1)));
        using var refused = await ReplaceAsync(reader, asset.Id, ("c.png", Files.Png(1, 1)));
        using var unknown = await ReplaceAsync(admin, Guid.NewGuid(), ("d.png", Files.Png(1, 1)));
        var current = await GetAsync(admin, $"{Root}/{asset.Id:D}");

        executable.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await CodeAsync(executable)).ShouldBe(MediaErrorCodes.Executable);
        two.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (current["fileName"]!.GetValue<string>(), current["checksum"]!.GetValue<string>()).ShouldBe(("bands.png", asset.Checksum));
        (await FilesOfAsync(media, asset)).Count.ShouldBe(1);
    }

    private static async Task<HttpResponseMessage> ReplaceAsync(HttpClient client, Guid id, params (string Name, byte[] Bytes)[] files)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "file", name);
        }

        return await client.PostAsync(new Uri($"{Root}/{id:D}/replace", UriKind.Relative), form, Token);
    }

    private static byte[] Solid(int width, int height, SKColor colour)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(colour);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // The files of the asset's directory in the local storage: its original and its variants.
    private static async Task<List<string>> FilesOfAsync(MediaHost media, MediaAsset asset)
    {
        await using var scope = media.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Asset>().AsNoTracking().AnyAsync(item => item.Id == asset.Id, Token)).ShouldBeTrue();
        var directory = Path.Combine(media.StorageRoot, asset.SiteId.ToString("N"), asset.Id.ToString("N"));
        return Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory)] : [];
    }
}
