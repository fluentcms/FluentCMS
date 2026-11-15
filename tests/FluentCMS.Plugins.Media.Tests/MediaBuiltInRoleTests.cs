using System.Net;
using System.Net.Http.Json;
using FluentCMS.Core.Authorization;
using FluentCMS.Testing;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

// The plugin names media.read and media.upload for the built-in Editor and Author by default, and media.update for the
// Editor, which a new instance gives them at its first start (#3103).
public sealed class MediaBuiltInRoleTests(MediaHost media) : IClassFixture<MediaHost>
{
    [Theory]
    [InlineData(BuiltInRoles.EditorName)]
    [InlineData(BuiltInRoles.AuthorName)]
    public async Task TheBuiltInEditorAndAuthorListAndUploadMedia(string role)
    {
        using var client = await KeyAsync(role);

        var uploaded = await UploadedAsync(client, null, ("logo.png", Files.Png(8, 8)));
        var listed = await GetAsync(client, Root);

        listed["items"]!.AsArray().Select(item => Id(item!)).ShouldContain(Id(uploaded.ShouldHaveSingleItem()!));
    }

    [Fact]
    public async Task OnlyTheEditorChangesTheMetadataOfAnAssetAndNeitherDeletesIt()
    {
        using var editor = await KeyAsync(BuiltInRoles.EditorName);
        using var author = await KeyAsync(BuiltInRoles.AuthorName);
        var asset = Id((await UploadedAsync(editor, null, ("photo.png", Files.Png(8, 8)))).ShouldHaveSingleItem()!);

        using var byEditor = await editor.PatchAsJsonAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), new { alt = "A photo" }, Token);
        using var byAuthor = await author.PatchAsJsonAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), new { alt = "Another photo" }, Token);
        using var deleted = await editor.DeleteAsync(new Uri($"{Root}/{asset:D}", UriKind.Relative), Token);

        byEditor.StatusCode.ShouldBe(HttpStatusCode.OK);
        byAuthor.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deleted.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> KeyAsync(string role) =>
        media.Host.AsKey(await media.Host.CreateApiKeyAsync(ApiKeyClass.Management, role, cancellationToken: Token));
}
