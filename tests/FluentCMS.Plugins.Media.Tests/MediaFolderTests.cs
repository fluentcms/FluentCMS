using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaFolderTests
{
    [Fact]
    public async Task FoldersNestAndMoveWithTheirSubfoldersAndAssets()
    {
        await using var media = new MediaHost();
        using var client = await media.AdminAsync();
        var archive = await FolderAsync(client, "archive");
        var photos = await FolderAsync(client, "photos");
        var summer = await FolderAsync(client, "summer", photos);
        var beach = await FolderAsync(client, "beach", summer);
        var asset = Id((await UploadedAsync(client, beach, ("sand.jpg", Files.Jpeg(1, 1)))).Single()!);

        var moved = await PostAsync(client, $"{Root}/folders/{summer:D}/move", new { parentId = archive });
        var renamed = await JsonAsync(await client.PatchAsJsonAsync(new Uri($"{Root}/folders/{summer:D}", UriKind.Relative), new { name = "summer 2026" }, Token));

        moved["parentId"]!.GetValue<string>().ShouldBe(archive.ToString("D"));
        renamed["name"]!.GetValue<string>().ShouldBe("summer 2026");
        (await GetAsync(client, $"{Root}?folder={archive:D}&subfolders=true"))["items"]!.AsArray().Select(item => Id(item!)).ShouldBe([asset]);
        (await GetAsync(client, $"{Root}?folder={photos:D}&subfolders=true"))["items"]!.AsArray().ShouldBeEmpty();
        (await GetAsync(client, $"{Root}/{asset:D}"))["folderId"]!.GetValue<string>().ShouldBe(beach.ToString("D"));

        await using var scope = media.Services.CreateAsyncScope();
        var folders = await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<Folder>().AsNoTracking().ToDictionaryAsync(folder => folder.Id, Token);
        folders[beach].Path.ShouldBe($"/{archive:D}/{summer:D}/{beach:D}/");
        folders[summer].Path.ShouldBe($"/{archive:D}/{summer:D}/");
    }

    [Fact]
    public async Task AFolderCannotMoveIntoItselfTakeATakenNameOrBeDeletedWhileItHoldsAnything()
    {
        await using var media = new MediaHost();
        using var client = await media.AdminAsync();
        var photos = await FolderAsync(client, "photos");
        var summer = await FolderAsync(client, "summer", photos);
        await FolderAsync(client, "other");

        using var cycle = await client.PostAsJsonAsync(new Uri($"{Root}/folders/{photos:D}/move", UriKind.Relative), new { parentId = summer }, Token);
        using var self = await client.PostAsJsonAsync(new Uri($"{Root}/folders/{photos:D}/move", UriKind.Relative), new { parentId = photos }, Token);
        using var taken = await client.PostAsJsonAsync(new Uri($"{Root}/folders", UriKind.Relative), new { name = "photos" }, Token);
        using var renamedOnto = await client.PatchAsJsonAsync(new Uri($"{Root}/folders/{photos:D}", UriKind.Relative), new { name = "other" }, Token);
        using var notEmpty = await client.DeleteAsync(new Uri($"{Root}/folders/{photos:D}", UriKind.Relative), Token);
        using var invalid = await client.PostAsJsonAsync(new Uri($"{Root}/folders", UriKind.Relative), new { name = "a/b" }, Token);

        cycle.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(cycle)).ShouldBe(MediaErrorCodes.FolderCycle);
        (await CodeAsync(self)).ShouldBe(MediaErrorCodes.FolderCycle);
        (await CodeAsync(taken)).ShouldBe(MediaErrorCodes.FolderNameTaken);
        (await CodeAsync(renamedOnto)).ShouldBe(MediaErrorCodes.FolderNameTaken);
        (await CodeAsync(notEmpty)).ShouldBe(MediaErrorCodes.FolderNotEmpty);
        (await CodeAsync(invalid)).ShouldBe(MediaErrorCodes.InvalidField);

        var asset = Id((await UploadedAsync(client, summer, ("a.png", Files.Png(1, 1)))).Single()!);
        using var holdsAsset = await client.DeleteAsync(new Uri($"{Root}/folders/{summer:D}", UriKind.Relative), Token);
        (await CodeAsync(holdsAsset)).ShouldBe(MediaErrorCodes.FolderNotEmpty);
        (await PostAsync(client, $"{Root}/{asset:D}/move", new { folderId = (Guid?)null }))["folderId"].ShouldBeNull();
        using var emptied = await client.DeleteAsync(new Uri($"{Root}/folders/{summer:D}", UriKind.Relative), Token);
        emptied.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AGrantLimitedToAFolderListsAndUploadsOnlyThereAndInItsSubfolders()
    {
        await using var media = new MediaHost();
        using var admin = await media.AdminAsync();
        var team = await FolderAsync(admin, "team");
        var drafts = await FolderAsync(admin, "drafts", team);
        var other = await FolderAsync(admin, "other");
        var inTeam = Id((await UploadedAsync(admin, team, ("a.png", Files.Png(1, 1)))).Single()!);
        var inOther = Id((await UploadedAsync(admin, other, ("b.png", Files.Png(1, 1)))).Single()!);
        await UploadedAsync(admin, null, ("c.png", Files.Png(1, 1)));
        using var editor = await media.ClientWithAsync(admin, new { permission = "media.*", constraints = new { resources = new[] { team.ToString("D") } } });

        var listed = (await GetAsync(editor, Root))["items"]!.AsArray().Select(item => Id(item!)).ToList();
        var folders = (await GetAsync(editor, $"{Root}/folders"))["items"]!.AsArray().Select(item => Id(item!)).ToList();
        var uploaded = await UploadedAsync(editor, drafts, ("d.png", Files.Png(1, 1)));
        using var elsewhere = await UploadAsync(editor, other, ("e.png", Files.Png(1, 1)));
        using var atRoot = await UploadAsync(editor, null, ("f.png", Files.Png(1, 1)));
        using var readOther = await editor.GetAsync(new Uri($"{Root}/{inOther:D}", UriKind.Relative), Token);
        using var moveOut = await editor.PostAsJsonAsync(new Uri($"{Root}/{inTeam:D}/move", UriKind.Relative), new { folderId = other }, Token);
        using var rootList = await editor.GetAsync(new Uri($"{Root}?folder=root", UriKind.Relative), Token);

        listed.ShouldBe([inTeam]);
        folders.ShouldBe([drafts, team], ignoreOrder: true);
        uploaded.Single()!["folderId"]!.GetValue<string>().ShouldBe(drafts.ToString("D"));
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(elsewhere)).ShouldBe(MediaErrorCodes.FolderNotFound);
        atRoot.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        readOther.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        moveOut.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await JsonAsync(rootList))["items"]!.AsArray().ShouldBeEmpty();
        (await GetAsync(editor, $"{Root}?folder={team:D}&subfolders=true"))["total"]!.GetValue<long>().ShouldBe(2);
    }

    [Fact]
    public async Task ACallerWithoutAMediaPermissionIsRefused()
    {
        await using var media = new MediaHost();
        using var admin = await media.AdminAsync();
        var asset = Id((await UploadedAsync(admin, null, ("a.png", Files.Png(1, 1)))).Single()!);
        using var reader = await media.ClientWithAsync(admin, new { permission = "media.read" });
        using var nobody = await media.ClientWithAsync(admin, new { permission = "schema.read" });

        using var list = await nobody.GetAsync(new Uri(Root, UriKind.Relative), Token);
        using var folders = await nobody.GetAsync(new Uri($"{Root}/folders", UriKind.Relative), Token);
        using var upload = await UploadAsync(reader, null, ("b.png", Files.Png(1, 1)));
        using var delete = await reader.DeleteAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), Token);
        using var folder = await reader.PostAsJsonAsync(new Uri($"{Root}/folders", UriKind.Relative), new { name = "x" }, Token);
        using var anonymous = await media.Host.CreateClient().GetAsync(new Uri(Root, UriKind.Relative), Token);

        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        folders.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        upload.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        folder.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(reader, $"{Root}/{asset:D}"))["fileName"]!.GetValue<string>().ShouldBe("a.png");
    }

    private static async Task<JsonNode> PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Token);
        return await JsonAsync(response);
    }
}
