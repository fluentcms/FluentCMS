using System.Net;
using System.Net.Http.Json;
using FluentCMS.Media;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaPresetTests(MediaHost media) : IClassFixture<MediaHost>
{
    private const string Presets = $"{Root}/presets";

    [Fact]
    public async Task APresetIsStoredListedAppliedByAUrlAndDeleted()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));
        var name = Unique("hero");

        using var put = await admin.PutAsJsonAsync(new Uri($"{Presets}/{name}", UriKind.Relative), new { width = 120, height = 60, fit = "fill", format = "png" }, Token);
        var stored = await JsonAsync(put);
        var listed = (await GetAsync(admin, Presets))["items"]!.AsArray();
        using var hero = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Preset = name }));
        using var narrower = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Preset = name, Width = 30 }));

        (stored["name"]!.GetValue<string>(), stored["options"]!["width"]!.GetValue<int>(), stored["options"]!["fit"]!.GetValue<string>(), stored["options"]!["format"]!.GetValue<string>()).ShouldBe((name, 120, "fill", "png"));
        listed.Select(item => item!["name"]!.GetValue<string>()).ShouldContain(name);
        var (type, bytes) = await ImageAsync(hero);
        type.ShouldBe("image/png");
        using (var image = Decode(bytes))
        {
            (image.Width, image.Height).ShouldBe((120, 60));
        }

        using (var image = Decode((await ImageAsync(narrower)).Bytes))
        {
            (image.Width, image.Height).ShouldBe((30, 60));
        }

        using var delete = await admin.DeleteAsync(new Uri($"{Presets}/{name}", UriKind.Relative), Token);
        using var gone = await FetchAsync(anonymous, await UrlAsync(media, asset, new MediaImageOptions { Preset = name }));
        using var again = await admin.DeleteAsync(new Uri($"{Presets}/{name}", UriKind.Relative), Token);

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(gone)).ShouldBe(MediaErrorCodes.PresetNotFound);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task APutReplacesThePresetWhoseChangeMakesANewVariant()
    {
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(400, 200));
        var url = await UrlAsync(media, asset, new MediaImageOptions { Preset = "thumb" });

        using (var first = await admin.PutAsJsonAsync(new Uri($"{Presets}/thumb", UriKind.Relative), new { width = 40 }, Token))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var before = await FetchAsync(anonymous, url);
        using (var second = await admin.PutAsJsonAsync(new Uri($"{Presets}/thumb", UriKind.Relative), new { width = 80 }, Token))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var after = await FetchAsync(anonymous, url);

        using (var image = Decode((await ImageAsync(before)).Bytes))
        {
            image.Width.ShouldBe(40);
        }

        using (var image = Decode((await ImageAsync(after)).Bytes))
        {
            image.Width.ShouldBe(80);
        }
    }

    [Fact]
    public async Task APresetIsCheckedAgainstTheRangesAndTheLimitsOfTheSite()
    {
        using var admin = await media.AdminAsync();
        await using var limits = await media.ConfigureAsync(settings => settings.MaxTransformWidth = 500);
        var name = Unique("hero");
        var stored = await NamesAsync(admin);

        using var badName = await admin.PutAsJsonAsync(new Uri($"{Presets}/Hero_1", UriKind.Relative), new { width = 10 }, Token);
        using var nested = await admin.PutAsJsonAsync(new Uri($"{Presets}/{name}", UriKind.Relative), new { preset = "other" }, Token);
        using var quality = await admin.PutAsJsonAsync(new Uri($"{Presets}/{name}", UriKind.Relative), new { quality = 0 }, Token);
        using var fit = await admin.PutAsJsonAsync(new Uri($"{Presets}/{name}", UriKind.Relative), new { fit = "crop" }, Token);
        using var avif = await admin.PutAsJsonAsync(new Uri($"{Presets}/{name}", UriKind.Relative), new { format = "avif" }, Token);
        using var wide = await admin.PutAsJsonAsync(new Uri($"{Presets}/{name}", UriKind.Relative), new { width = 501 }, Token);

        foreach (var (response, path) in (IEnumerable<(HttpResponseMessage, string)>)[(badName, "name"), (nested, "preset"), (quality, "quality"), (fit, "fit"), (avif, "format")])
        {
            var errors = (await JsonAsync(response, HttpStatusCode.UnprocessableEntity))["errors"]!.AsArray();
            errors.Select(error => (error!["path"]!.GetValue<string>(), error["code"]!.GetValue<string>())).ShouldContain((path, MediaErrorCodes.InvalidField));
        }

        wide.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(wide)).ShouldBe(MediaErrorCodes.TransformLimit);
        (await NamesAsync(admin)).ShouldBe(stored);
    }

    [Fact]
    public async Task PresetsNeedTheirPermissionWithoutAFolderLimitAndReadingThemNeedsMediaRead()
    {
        using var admin = await media.AdminAsync();
        var folder = await FolderAsync(admin, "Team");
        using var reader = await media.ClientWithAsync(admin, new { permission = "media.read" });
        using var limited = await media.ClientWithAsync(admin, new { permission = "media.*", constraints = new { resources = new[] { folder.ToString("D") } } });
        using var manager = await media.ClientWithAsync(admin, new { permission = "media.presets.manage" });
        using var nobody = await media.ClientWithAsync(admin, new { permission = "schema.read" });
        var name = Unique("hero");

        using var readerPut = await reader.PutAsJsonAsync(new Uri($"{Presets}/{name}", UriKind.Relative), new { width = 10 }, Token);
        using var limitedPut = await limited.PutAsJsonAsync(new Uri($"{Presets}/{name}", UriKind.Relative), new { width = 10 }, Token);
        using var managerPut = await manager.PutAsJsonAsync(new Uri($"{Presets}/{name}", UriKind.Relative), new { width = 10 }, Token);
        using var readerList = await reader.GetAsync(new Uri(Presets, UriKind.Relative), Token);
        using var nobodyList = await nobody.GetAsync(new Uri(Presets, UriKind.Relative), Token);
        using var limitedDelete = await limited.DeleteAsync(new Uri($"{Presets}/{name}", UriKind.Relative), Token);

        readerPut.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        limitedPut.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        managerPut.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(readerList))["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>()).ShouldContain(name);
        nobodyList.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        limitedDelete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<List<string>> NamesAsync(HttpClient client) =>
        [.. (await GetAsync(client, Presets))["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>())];
}
