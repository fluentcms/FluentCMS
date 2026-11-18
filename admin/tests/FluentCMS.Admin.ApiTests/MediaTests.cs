using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Plugins.Media.Admin;
using FluentCMS.Plugins.Media.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The media library's calls (spec 4.14) through the admin's client against the server half, as the roles that use it.
public sealed class MediaTests
{
    // A PNG of one pixel.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static CancellationToken Token => AdminHost.Token;

    // An upload as the library sends it: one file in the body the half writes.
    private static Task<MediaAssetSaved> UploadAsync(MediaApi media, Guid? folder, string fileName, byte[] bytes)
    {
        var body = MediaUploadBody.Create(folder, fileName, bytes.Length);
        bytes.CopyTo(body.File);
        return media.UploadAsync(body, Token);
    }

    [Fact]
    public async Task FilesUploadOneARequestIntoAFolderAndARefusedOneSaysWhy()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var media = new MediaApi(client.Api);

        var photos = await media.CreateFolderAsync("Photos", null, Token);
        var uploaded = await UploadAsync(media, photos.Id, "pixel.png", Png);
        var refused = await Should.ThrowAsync<ApiProblemException>(() => UploadAsync(media, photos.Id, "setup.exe", "MZ\u0090\0\u0003\0\0\0"u8.ToArray()));
        await UploadAsync(media, null, "root.png", Png);
        var named = await UploadAsync(media, photos.Id, "Café \"menu\" 2026.png", Png);

        uploaded.FileName.ShouldBe("pixel.png");
        uploaded.FolderId.ShouldBe(photos.Id);
        refused.Problem.Code.ShouldBe("FCMS1583");
        named.FileName.ShouldBe("Café \"menu\" 2026.png");

        var inPhotos = await media.ListAsync(new MediaListQuery { Folder = photos.Id }, Token);
        var asset = inPhotos.Items.Single(item => item.Id == uploaded.Id);
        asset.Id.ShouldBe(uploaded.Id);
        asset.MimeType.ShouldBe("image/png");
        asset.Width.ShouldBe(1);
        asset.Urls.Original.ShouldStartWith($"/media/{uploaded.Id:D}/original/");
        asset.Urls.Thumbnail.ShouldNotBeNull();
        inPhotos.Total.ShouldBe(2);

        // The Blog sample has assets of its own at the root.
        var atRoot = await media.ListAsync(new MediaListQuery(), Token);
        atRoot.Items.Select(item => item.FileName).ShouldContain("root.png");
        atRoot.Items.Select(item => item.FileName).ShouldNotContain("pixel.png");
        var found = await media.ListAsync(new MediaListQuery { Search = "pixel", Type = "image/*" }, Token);
        found.Items.Select(item => item.Id).ShouldBe([uploaded.Id]);
    }

    [Fact]
    public async Task AFolderIsCreatedRenamedMovedAndDeletedOnlyOnceEmpty()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var media = new MediaApi(client.Api);

        var archive = await media.CreateFolderAsync("Archive", null, Token);
        var photos = await media.CreateFolderAsync("Photos", null, Token);
        var taken = await Should.ThrowAsync<ApiProblemException>(() => media.CreateFolderAsync("Photos", null, Token));
        (await media.RenameFolderAsync(photos.Id, "Pictures", Token)).Name.ShouldBe("Pictures");
        (await media.MoveFolderAsync(photos.Id, archive.Id, Token)).ParentId.ShouldBe(archive.Id);
        var cycle = await Should.ThrowAsync<ApiProblemException>(() => media.MoveFolderAsync(archive.Id, photos.Id, Token));

        var asset = await UploadAsync(media, photos.Id, "pixel.png", Png);
        var notEmpty = await Should.ThrowAsync<ApiProblemException>(() => media.DeleteFolderAsync(photos.Id, Token));
        (await media.MoveAsync(asset.Id, null, Token)).FolderId.ShouldBeNull();
        await media.DeleteFolderAsync(photos.Id, Token);

        taken.Problem.Code.ShouldBe("FCMS1587");
        cycle.Problem.Code.ShouldBe("FCMS1589");
        notEmpty.Problem.Code.ShouldBe("FCMS1588");
        (await media.FoldersAsync(Token)).Select(folder => folder.Name).ShouldBe(["Archive"]);
        (await media.ListAsync(new MediaListQuery(), Token)).Items.Select(item => item.Id).ShouldContain(asset.Id);
    }

    [Fact]
    public async Task TheEditorBrowsesUploadsAndMovesButMayNotManageFolders()
    {
        await using var admin = new AdminHost();
        using var owner = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var photos = await new MediaApi(owner.Api).CreateFolderAsync("Photos", null, Token);
        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var media = new MediaApi(editor.Api);

        var asset = await UploadAsync(media, null, "pixel.png", Png);
        (await media.MoveAsync(asset.Id, photos.Id, Token)).FolderId.ShouldBe(photos.Id);
        (await media.FoldersAsync(Token)).Select(folder => folder.Id).ShouldBe([photos.Id]);
        var refused = await Should.ThrowAsync<ApiProblemException>(() => media.CreateFolderAsync("Mine", null, Token));

        refused.Problem.Status.ShouldBe(403);
        var access = await editor.Client.GetMyAccessAsync(null, null, Token);
        access.Permissions.ShouldContain("media.upload");
        access.Permissions.ShouldContain("media.update");
        access.Permissions.ShouldNotContain("media.folders.manage");
    }

    // The picker and the field of A-20 as the built-in roles that edit content use them (#3103): the picker reads and
    // uploads, the field reads its asset in the content locale and saves its id, and a list populates the field for its cell.
    [Theory]
    [InlineData(BuiltInRoles.EditorName)]
    [InlineData(BuiltInRoles.AuthorName)]
    public async Task TheEditorAndTheAuthorPickAndUploadAnImageForAMediaFieldThatAListPopulates(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var media = new MediaApi(client.Api);

        (await media.ListAsync(new MediaListQuery { Type = "image/*", TypeIsConstraint = true }, Token)).Items.ShouldNotBeEmpty();
        var uploaded = await UploadAsync(media, null, "hero.png", Png);
        var asset = await media.GetAsync(uploaded.Id, "en", Token);
        asset.Urls.Thumbnail.ShouldNotBeNull();

        var created = await client.Client.CreateEntryAsync("article", new JsonObject
        {
            ["title"] = "With a hero " + role,
            ["hero"] = MediaFieldValues.Write([asset.Id], multiple: false),
        }, client.Client.StartAction("en"), Token);
        MediaFieldValues.Ids(created.Entry!.Fields["hero"]).ShouldBe([asset.Id]);

        var page = await client.Client.ListEntriesAsync("article", new ContentQuery
        {
            Filter = new FieldFilter("id", FluentCMS.Admin.Lists.FilterOperator.Equal, created.Entry.Id),
            Fields = ["title", "hero"],
            Populate = ["hero"],
        }, Token);
        var shown = MediaFieldValues.Assets(page.Items.Single().Fields["hero"]).ShouldHaveSingleItem();
        shown.Id.ShouldBe(asset.Id);
        shown.FileName.ShouldBe("hero.png");
        shown.MimeType.ShouldBe("image/png");
        shown.Urls!.Original.ShouldStartWith($"/media/{asset.Id:D}/original/");
    }
}
