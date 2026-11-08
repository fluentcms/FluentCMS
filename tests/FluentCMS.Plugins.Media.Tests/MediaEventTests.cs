using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaEventTests
{
    [Fact]
    public async Task EachUploadUpdateMoveAndDeleteRaisesItsEventAndWritesAnActivityRow()
    {
        await using var media = new MediaHost();
        using var client = await media.AdminAsync();
        var folder = await FolderAsync(client, "photos");
        var id = Id((await UploadedAsync(client, null, ("photo.jpg", Files.Jpeg(4, 4)))).Single()!);
        (await client.PatchAsJsonAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), new { title = "Harbour" }, Token)).Dispose();
        (await client.PatchAsJsonAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), new { }, Token)).Dispose();
        (await client.PostAsJsonAsync(new Uri($"{Root}/{id:D}/move", UriKind.Relative), new { folderId = folder }, Token)).Dispose();
        (await client.DeleteAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token)).Dispose();

        await media.DrainOutboxAsync();
        var rows = (await GetAsync(client, "/api/management/v1/activity?targetType=media"))["items"]!.AsArray();

        rows.Select(row => row!["action"]!.GetValue<string>()).ShouldBe(["media.deleted", "media.moved", "media.updated", "media.uploaded"]);
        rows.ShouldAllBe(row => row!["targetId"]!.GetValue<string>() == id.ToString("D") && row["targetLabel"]!.GetValue<string>() == "photo.jpg");
        rows.Single(row => row!["action"]!.GetValue<string>() == "media.moved")!["details"]!["to"]!.GetValue<string>().ShouldBe(folder.ToString("D"));
        rows.Single(row => row!["action"]!.GetValue<string>() == "media.uploaded")!["details"]!["mimeType"]!.GetValue<string>().ShouldBe("image/jpeg");
    }

    [Fact]
    public async Task TheStreamsMigrationAppliesAndMatchesTheModel()
    {
        await using var media = new MediaHost();

        await using var scope = media.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();
        var applied = await database.Database.GetAppliedMigrationsAsync(Token);
        applied.Select(migration => migration[15..]).ShouldBe(["InitialMedia", "MediaTransforms"]);
        database.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
