using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Plugins.Media.Admin;
using FluentCMS.Plugins.Media.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The calls of an asset's detail (spec 4.14, A-50) through the admin's client against the server half.
public sealed class MediaDetailTests
{
    // Four by two pixels, the left half black and the right half white.
    private static readonly byte[] Halves = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAIAAADwyuo0AAAAEUlEQVR4nGNgAIP/YMCAzAEAd6IL9cTZu14AAAAASUVORK5CYII=");

    // Two by two red pixels.
    private static readonly byte[] Red = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGP4z8AARAwQCgAf7gP9i18U1AAAAABJRU5ErkJggg==");

    private static CancellationToken Token => AdminHost.Token;

    private static MediaUploadBody Body(Guid? folder, string fileName, byte[] bytes)
    {
        var body = MediaUploadBody.Create(folder, fileName, bytes.Length);
        bytes.CopyTo(body.File);
        return body;
    }

    private static async Task<MediaAssetInfo> UploadAsync(MediaApi media, string fileName, byte[] bytes)
    {
        var saved = await media.UploadAsync(Body(null, fileName, bytes), Token);
        return await media.GetAsync(saved.Id, "en", Token);
    }

    [Fact]
    public async Task TheAltTextAndCaptionOfEachLocaleAreKeptApartAndASaveAfterAnotherTabsIsAConflict()
    {
        await using var admin = new AdminHost();
        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var media = new MediaApi(editor.Api);
        var english = await UploadAsync(media, "cat.png", Red);

        english = await media.UpdateAsync(english, new JsonObject { ["alt"] = "A cat", ["caption"] = "On a mat" }, Token);
        // The ETag of a save's answer is the base of the next.
        english = await media.UpdateAsync(english, new JsonObject { ["title"] = "Cat" }, Token);
        var french = await media.GetAsync(english.Id, "fr", Token);
        french.Alt.ShouldBeNull();
        french = await media.UpdateAsync(french, new JsonObject { ["alt"] = "Un chat", ["caption"] = "Sur un tapis" }, Token);

        var en = await media.GetAsync(english.Id, "en", Token);
        var fr = await media.GetAsync(english.Id, "fr", Token);
        (en.Locale, en.Alt, en.Caption, en.Title).ShouldBe(("en", "A cat", "On a mat", "Cat"));
        (fr.Locale, fr.Alt, fr.Caption, fr.Title).ShouldBe(("fr", "Un chat", "Sur un tapis", "Cat"));

        // Another tab saves after this one read the asset.
        using var other = await admin.SignInAsync(BuiltInRoles.EditorName);
        await new MediaApi(other.Api).UpdateAsync(await new MediaApi(other.Api).GetAsync(en.Id, "en", Token), new JsonObject { ["credit"] = "Jo" }, Token);
        var conflict = await Should.ThrowAsync<MediaConflictException>(() => media.UpdateAsync(en, new JsonObject { ["credit"] = "Sam" }, Token));
        conflict.Current.Credit.ShouldBe("Jo");
        (await media.GetAsync(en.Id, "en", Token)).Credit.ShouldBe("Jo");

        // Saving over it is based on the asset the conflict holds.
        (await media.UpdateAsync(conflict.Current, new JsonObject { ["credit"] = "Sam" }, Token)).Credit.ShouldBe("Sam");
    }

    [Fact]
    public async Task TheFocalPointIsWhatACoverPresetKeeps()
    {
        await using var admin = new AdminHost();
        using var owner = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var media = new MediaApi(owner.Api);
        var asset = await UploadAsync(media, "halves.png", Halves);
        await media.SetPresetAsync("square", new MediaPresetOptions { Width = 2, Height = 2, Fit = "cover", Format = "png" }, Token);
        using var anonymous = new HttpClient(admin.ServerHandler()) { BaseAddress = new Uri("http://localhost/") };

        async Task<byte[]> SquareAsync()
        {
            var delivered = JsonNode.Parse(await anonymous.GetStringAsync($"api/delivery/v1/media/{asset.Id:D}", Token))!;
            var url = delivered["urls"]!["presets"]!["square"]!.GetValue<string>();
            return await anonymous.GetByteArrayAsync(url.TrimStart('/'), Token);
        }

        asset = await media.UpdateAsync(asset, new JsonObject { ["focalPoint"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.5 } }, Token);
        var left = await SquareAsync();
        asset = await media.UpdateAsync(asset, new JsonObject { ["focalPoint"] = new JsonObject { ["x"] = 1.0, ["y"] = 0.5 } }, Token);
        var right = await SquareAsync();

        asset.FocalPoint.ShouldBe(new MediaPoint { X = 1, Y = 0.5 });
        left.ShouldNotBe(right);
    }

    [Fact]
    public async Task UsageNamesTheArticlesAndADeleteOfAnAssetInUseNeedsForce()
    {
        await using var admin = new AdminHost();
        using var owner = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var media = new MediaApi(owner.Api);
        var asset = await UploadAsync(media, "hero.png", Red);
        var article = await owner.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "Cats at home", ["hero"] = asset.Id.ToString("D") }, owner.Client.StartAction(), Token);

        var usage = await media.UsageAsync(asset.Id, Token);
        var rows = await MediaUsage.RowsAsync(media, usage, "en", Token);

        usage.Single().EntryId.ShouldBe(article.Entry!.Id);
        rows.Single().Label.ShouldBe("Cats at home");
        rows.Single().Href.ShouldStartWith($"content/article/{article.Entry.Id:D}");
        var refused = await Should.ThrowAsync<ApiProblemException>(() => media.DeleteAsync(asset.Id, force: false, Token));
        refused.Problem.Code.ShouldBe("FCMS1594");
        await media.DeleteAsync(asset.Id, force: true, Token);
        (await Should.ThrowAsync<ApiProblemException>(() => media.GetAsync(asset.Id, "en", Token))).Problem.Status.ShouldBe(404);
    }

    [Fact]
    public async Task AReplacedAssetHasANewThumbnail()
    {
        await using var admin = new AdminHost();
        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var media = new MediaApi(editor.Api);
        var asset = await UploadAsync(media, "pixel.png", Red);

        var answer = await media.ReplaceAsync(asset.Id, Body(null, "halves.png", Halves), Token);
        var replaced = await media.GetAsync(asset.Id, "en", Token);

        answer.ETag.ShouldBe(replaced.ETag);
        answer.ETag.ShouldNotBe(asset.ETag);

        replaced.Id.ShouldBe(asset.Id);
        replaced.Width.ShouldBe(4);
        replaced.Urls.Thumbnail.ShouldNotBe(asset.Urls.Thumbnail);
    }

    [Fact]
    public async Task APrivateAssetDownloadsThroughASignedUrlAndItsOriginalAnswersNoAnonymousRequest()
    {
        await using var admin = new AdminHost();
        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var media = new MediaApi(editor.Api);
        var asset = await UploadAsync(media, "contract.png", Red);
        var publicOriginal = asset.Urls.Original;

        asset = await media.UpdateAsync(asset, new JsonObject { ["isPrivate"] = true }, Token);
        var download = await media.DownloadUrlAsync(asset.Id, Token);
        using var anonymous = new HttpClient(admin.ServerHandler()) { BaseAddress = new Uri("http://localhost/") };

        asset.IsPrivate.ShouldBeTrue();
        using var signed = await anonymous.GetAsync(download.Url.TrimStart('/'), Token);
        signed.StatusCode.ShouldBe(HttpStatusCode.OK);
        signed.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        (await signed.Content.ReadAsByteArrayAsync(Token)).ShouldBe(Red);
        using var unsigned = await anonymous.GetAsync(publicOriginal.TrimStart('/'), Token);
        unsigned.IsSuccessStatusCode.ShouldBeFalse();
        using var bare = await anonymous.GetAsync($"media/{asset.Id:D}/original/contract.png", Token);
        bare.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task AnAdministratorManagesPresetsAndAnEditorMayNot()
    {
        await using var admin = new AdminHost();
        using var owner = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var media = new MediaApi(owner.Api);

        var card = await media.SetPresetAsync("card", new MediaPresetOptions { Width = 400, Height = 300, Fit = "cover", Quality = 70, Format = "webp" }, Token);
        (await media.PresetsAsync(Token)).Select(preset => preset.Name).ShouldContain("card");
        card.Options.ShouldBe(new MediaPresetOptions { Width = 400, Height = 300, Fit = "cover", Quality = 70, Format = "webp" });

        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var refused = await Should.ThrowAsync<ApiProblemException>(() => new MediaApi(editor.Api).SetPresetAsync("mine", new MediaPresetOptions { Width = 10 }, Token));
        refused.Problem.Status.ShouldBe(403);
        (await editor.Client.GetMyAccessAsync(null, null, Token)).Permissions.ShouldNotContain("media.presets.manage");
        (await owner.Client.GetMyAccessAsync(null, null, Token)).Permissions.ShouldContain("media.presets.manage");

        await media.DeletePresetAsync("card", Token);
        (await media.PresetsAsync(Token)).Select(preset => preset.Name).ShouldNotContain("card");
    }
}
