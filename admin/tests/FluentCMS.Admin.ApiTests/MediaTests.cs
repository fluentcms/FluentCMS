using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
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

    [Fact]
    public async Task FilesUploadOneARequestIntoAFolderAndARefusedOneSaysWhy()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var media = new MediaApi(client.Api);

        var photos = await media.CreateFolderAsync("Photos", null, Token);
        var uploaded = await media.UploadAsync(photos.Id, "pixel.png", Png, Token);
        var refused = await Should.ThrowAsync<ApiProblemException>(() => media.UploadAsync(photos.Id, "setup.exe", "MZ\u0090\0\u0003\0\0\0"u8.ToArray(), Token));
        await media.UploadAsync(null, "root.png", Png, Token);

        uploaded.FileName.ShouldBe("pixel.png");
        uploaded.FolderId.ShouldBe(photos.Id);
        refused.Problem.Code.ShouldBe("FCMS1583");

        var inPhotos = await media.ListAsync(new MediaListQuery { Folder = photos.Id }, Token);
        var asset = inPhotos.Items.ShouldHaveSingleItem();
        asset.Id.ShouldBe(uploaded.Id);
        asset.MimeType.ShouldBe("image/png");
        asset.Width.ShouldBe(1);
        asset.Urls.Original.ShouldStartWith($"/media/{uploaded.Id:D}/original/");
        asset.Urls.Thumbnail.ShouldNotBeNull();
        inPhotos.Total.ShouldBe(1);

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

        var asset = await media.UploadAsync(photos.Id, "pixel.png", Png, Token);
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

        var asset = await media.UploadAsync(null, "pixel.png", Png, Token);
        (await media.MoveAsync(asset.Id, photos.Id, Token)).FolderId.ShouldBe(photos.Id);
        (await media.FoldersAsync(Token)).Select(folder => folder.Id).ShouldBe([photos.Id]);
        var refused = await Should.ThrowAsync<ApiProblemException>(() => media.CreateFolderAsync("Mine", null, Token));

        refused.Problem.Status.ShouldBe(403);
        var access = await editor.Client.GetMyAccessAsync(null, null, Token);
        access.Permissions.ShouldContain("media.upload");
        access.Permissions.ShouldContain("media.update");
        access.Permissions.ShouldNotContain("media.folders.manage");
    }
}
