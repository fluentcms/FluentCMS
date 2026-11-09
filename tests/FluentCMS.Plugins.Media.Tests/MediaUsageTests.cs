using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.Storage;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaUsageTests(PostHost media) : IClassFixture<PostHost>
{
    private static readonly string[] ReadableFields = ["title", "hero", "content"];

    [Fact]
    public async Task UsageListsTheFieldsBlocksAndRichTextThatReferToTheAssetWithoutWhatTheCallerCannotRead()
    {
        using var admin = await media.AdminAsync();
        var asset = Id((await UploadedAsync(admin, null, ("hero.png", Files.Png(4, 4)))).Single()!);
        var other = Id((await UploadedAsync(admin, null, ("other.png", Files.Png(4, 4)))).Single()!);
        var post = await EntryAsync(admin, "post", "Harbour");
        var secret = await EntryAsync(admin, "secret", "Hidden");
        var trashed = await EntryAsync(admin, "post", "Gone");
        (await admin.DeleteAsync(new Uri($"/api/management/v1/content/post/{trashed:D}", UriKind.Relative), Token)).Dispose();
        await media.ReferAsync("post", post, "hero", asset);
        await media.ReferAsync("post", post, "hero", asset, ContentState.Published);
        await media.ReferAsync("post", post, "body.image", asset);
        await media.ReferAsync("post", post, "content", asset);
        await media.ReferAsync("secret", secret, "hero", asset);
        await media.ReferAsync("post", trashed, "hero", asset);
        await media.ReferAsync("post", post, "thumbnail", other);
        using var reader = await media.ClientWithAsync(
            admin,
            new { permission = "media.read" },
            new { permission = "content.post.read", constraints = new { fields = ReadableFields } });

        var all = Usage(await GetAsync(admin, $"{Root}/{asset:D}/usage"));
        var readable = Usage(await GetAsync(reader, $"{Root}/{asset:D}/usage"));

        all.ShouldBe(
        [
            ("post", post, "published", "hero"),
            ("post", post, "draft", "body.image"),
            ("post", post, "draft", "content"),
            ("post", post, "draft", "hero"),
            ("secret", secret, "draft", "hero"),
        ]);
        readable.ShouldBe([("post", post, "published", "hero"), ("post", post, "draft", "content"), ("post", post, "draft", "hero")]);
    }

    [Fact]
    public async Task UsageOfAnAssetOutsideTheCallersFoldersIsNotFound()
    {
        using var admin = await media.AdminAsync();
        var team = await FolderAsync(admin, "team");
        var asset = Id((await UploadedAsync(admin, null, ("a.png", Files.Png(1, 1)))).Single()!);
        using var editor = await media.ClientWithAsync(admin, new { permission = "media.read", constraints = new { resources = new[] { team.ToString("D") } } });

        using var response = await editor.GetAsync(new Uri($"{Root}/{asset:D}/usage", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(response)).ShouldBe(MediaErrorCodes.AssetNotFound);
    }

    [Fact]
    public async Task DeletingAReferencedAssetIsRefusedAndForceDeletesItAndItsReferences()
    {
        using var admin = await media.AdminAsync();
        var asset = Id((await UploadedAsync(admin, null, ("hero.png", Files.Png(4, 4)))).Single()!);
        var post = await EntryAsync(admin, "post", "Harbour");
        var secret = await EntryAsync(admin, "secret", "Hidden");
        await media.ReferAsync("post", post, "hero", asset);
        await media.ReferAsync("post", post, "hero", asset, ContentState.Published);
        await media.ReferAsync("secret", secret, "content", asset);

        using var refused = await admin.DeleteAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), Token);
        var stillThere = await GetAsync(admin, $"{Root}/{asset:D}");
        using var forced = await admin.DeleteAsync(new Uri($"{Root}/{asset:D}?force=true", UriKind.Relative), Token);
        using var gone = await admin.GetAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), Token);
        await media.DrainOutboxAsync();
        var evicted = (await GetAsync(admin, "/api/management/v1/activity?action=cache.invalidated"))["items"]!.AsArray()
            .Select(row => row!["details"]!["tags"]!.GetValue<string>()).ToList();

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(refused)).ShouldBe(MediaErrorCodes.AssetInUse);
        Id(stillThere).ShouldBe(asset);
        forced.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await media.ReferencesToAsync(asset)).ShouldBe(0);
        evicted.ShouldContain(tags => tags.Split(',', StringSplitOptions.None).Order().SequenceEqual(new[] { $"entry:{post:D}", $"entry:{secret:D}" }.Order()));
        evicted.ShouldContain($"media:{asset:D}");
    }

    [Fact]
    public async Task OnlyAReferenceFromTheTrashDoesNotHoldTheDeleteBack()
    {
        using var admin = await media.AdminAsync();
        var asset = Id((await UploadedAsync(admin, null, ("hero.png", Files.Png(4, 4)))).Single()!);
        var trashed = await EntryAsync(admin, "post", "Gone");
        (await admin.DeleteAsync(new Uri($"/api/management/v1/content/post/{trashed:D}", UriKind.Relative), Token)).Dispose();
        await media.ReferAsync("post", trashed, "hero", asset);

        using var deleted = await admin.DeleteAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), Token);

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await media.ReferencesToAsync(asset)).ShouldBe(1);
    }

    [Fact]
    public async Task TheReferenceReaderRefusesAContentTypeAsATarget()
    {
        await using var scope = media.Services.CreateAsyncScope();
        var references = scope.ServiceProvider.GetRequiredService<FluentCMS.Content.IContentReferences>();
        var rows = await scope.ServiceProvider.GetRequiredService<CmsDbContext>().References.CountAsync(Token);

        await Should.ThrowAsync<ArgumentException>(() => references.RemoveAsync("post", Guid.NewGuid(), Token));
        await Should.ThrowAsync<ArgumentException>(() => references.CountAsync("", Guid.NewGuid(), Token));
        (await scope.ServiceProvider.GetRequiredService<CmsDbContext>().References.CountAsync(Token)).ShouldBe(rows);
    }

    private static List<(string Type, Guid Entry, string Status, string Field)> Usage(JsonNode body) =>
        [.. body["items"]!.AsArray().Select(item => (item!["type"]!.GetValue<string>(), Id(item, "entryId"), item["status"]!.GetValue<string>(), item["field"]!.GetValue<string>()))];
}
