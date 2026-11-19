using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;

namespace FluentCMS.Plugins.Media.Tests;

// The writes of an asset take If-Match as the writes of an entry do: the version read as the ETag, refused with 412 when
// the asset has moved on since, and no check at all without the header.
public sealed class MediaIfMatchTests(MediaHost media) : IClassFixture<MediaHost>
{
    private const string VersionConflict = "FCMS3105";
    private const string IfMatchInvalid = "FCMS3131";

    // Few enough writers that SQLite, which takes one writer at a time, serves them all well within its busy timeout.
    private const int Writers = 4;

    public static TheoryData<string> Writes => ["patch", "move", "replace", "delete"];

    [Fact]
    public async Task AReadGivesTheVersionAsTheETagAndEachWriteMovesItOn()
    {
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);

        var read = await ReadAsync(client, id);
        read.ShouldBe("\"1\"");

        using var patched = await WriteAsync(client, "patch", id, read);
        await JsonAsync(patched);
        patched.Headers.ETag!.Tag.ShouldBe("\"2\"");
        (await ReadAsync(client, id)).ShouldBe("\"2\"");
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task AWriteWithAStaleIfMatchIsRefusedAndChangesNothing(string write)
    {
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);
        var stale = await ReadAsync(client, id);
        using (var other = await WriteAsync(client, "patch", id, stale, title: "Other tab"))
        {
            await JsonAsync(other);
        }

        var before = await GetAsync(client, $"{Root}/{id:D}");
        using var refused = await WriteAsync(client, write, id, stale);

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed, await refused.Content.ReadAsStringAsync(Token));
        (await CodeAsync(refused)).ShouldBe(VersionConflict);
        (await refused.Content.ReadAsStringAsync(Token)).ShouldContain("the asset is at version 2");
        var after = await GetAsync(client, $"{Root}/{id:D}");
        after.ToJsonString().ShouldBe(before.ToJsonString());
        (await ReadAsync(client, id)).ShouldBe("\"2\"");
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task AWriteWithTheCurrentIfMatchIsAppliedAndGivesTheNextVersion(string write)
    {
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);
        var current = await ReadAsync(client, id);

        using var response = await WriteAsync(client, write, id, current);

        if (write == "delete")
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using var gone = await client.GetAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token);
            gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            return;
        }

        var written = await JsonAsync(response);
        response.Headers.ETag!.Tag.ShouldBe("\"2\"");
        Id(written).ShouldBe(id);
        (await ReadAsync(client, id)).ShouldBe("\"2\"");
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task AWriteWithoutIfMatchIsAppliedOverAnyVersion(string write)
    {
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);
        using (var other = await WriteAsync(client, "patch", id, null, title: "Other tab"))
        {
            await JsonAsync(other);
        }

        using var response = await WriteAsync(client, write, id, null);

        response.StatusCode.ShouldBe(write == "delete" ? HttpStatusCode.NoContent : HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        if (write != "delete")
        {
            response.Headers.ETag!.Tag.ShouldBe("\"3\"");
        }
    }

    [Fact]
    public async Task AWeakTagAndAStarAreReadAsEntriesReadThem()
    {
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);

        using (var weak = await WriteAsync(client, "patch", id, "W/\"1\""))
        {
            await JsonAsync(weak);
        }

        using (var any = await WriteAsync(client, "patch", id, "*"))
        {
            await JsonAsync(any);
            any.Headers.ETag!.Tag.ShouldBe("\"3\"");
        }

        foreach (var write in (string[])["patch", "move", "replace", "delete"])
        {
            using var malformed = await WriteAsync(client, write, id, "\"3\", \"4\"");
            malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest, write);
            (await CodeAsync(malformed)).ShouldBe(IfMatchInvalid, write);
        }

        (await ReadAsync(client, id)).ShouldBe("\"3\"");
    }

    [Fact]
    public async Task AStaleReplaceWritesNoFile()
    {
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);
        using (var other = await WriteAsync(client, "patch", id, null, title: "Other tab"))
        {
            await JsonAsync(other);
        }

        var before = Files();
        using var refused = await WriteAsync(client, "replace", id, "\"1\"");

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        Files().ShouldBe(before, ignoreOrder: true);

        string[] Files() => [.. Directory.EnumerateFiles(media.StorageRoot, "*", SearchOption.AllDirectories).Where(file => file.Contains(id.ToString("N"), StringComparison.Ordinal))];
    }

    // An asset outside what the caller reaches is not found, whatever version it names.
    [Fact]
    public async Task ACallerOutsideTheAssetsFolderGetsNotFoundRatherThanAConflict()
    {
        using var admin = await media.AdminAsync();
        var folder = await FolderAsync(admin, Unique("theirs"));
        var id = await AssetAsync(admin);
        using var outsider = await media.ClientWithAsync(admin, new { permission = "media.update", constraints = new { resources = new[] { folder.ToString("D") } } });

        using var response = await WriteAsync(outsider, "patch", id, "\"7\"");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync(admin, id)).ShouldBe("\"1\"");
    }

    // A forced delete takes the asset out of the entries that use it; one refused for its version leaves them.
    [Fact]
    public async Task AForcedDeleteWithAStaleIfMatchLeavesTheReferences()
    {
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);
        await media.ReferAsync("post", Guid.NewGuid(), "hero", id);
        using (var other = await WriteAsync(client, "patch", id, null, title: "Other tab"))
        {
            await JsonAsync(other);
        }

        using (var refused = await SendDeleteAsync(client, id, "\"1\""))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        }

        (await media.ReferencesToAsync(id)).ShouldBe(1);
        (await ReadAsync(client, id)).ShouldBe("\"2\"");

        using var deleted = await SendDeleteAsync(client, id, "\"2\"");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await media.ReferencesToAsync(id)).ShouldBe(0);
    }

    // The version is the row's concurrency token, so of writes that all read the same version exactly one is applied, even
    // when they all pass the comparison before any of them saves.
    [Fact]
    public async Task OfConcurrentWritesBasedOnOneVersionOnlyOneIsApplied()
    {
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);
        var read = await ReadAsync(client, id);

        var responses = await Task.WhenAll(Enumerable.Range(0, Writers).Select(index => WriteAsync(client, "patch", id, read, title: $"Tab {index}")));

        try
        {
            responses.Select(response => response.StatusCode).Order().ShouldBe([HttpStatusCode.OK, .. Enumerable.Repeat(HttpStatusCode.PreconditionFailed, Writers - 1)]);
            (await ReadAsync(client, id)).ShouldBe("\"2\"");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task ConcurrentWritesWithoutIfMatchAreAllApplied()
    {
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, Writers).Select(index => WriteAsync(client, "patch", id, null, title: $"Tab {index}")));

        try
        {
            responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
            (await ReadAsync(client, id)).ShouldBe($"\"{Writers + 1}\"");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    // A write that lost the race is applied again to the row the other saved, so the alt text of one locale saved at the same
    // time as another's keeps both, though both are in one column.
    [Fact]
    public async Task ConcurrentSavesOfTwoLocalesWithoutIfMatchBothSurvive()
    {
        await media.AddLocaleAsync("fr");
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);

        for (var round = 0; round < 5; round++)
        {
            var saves = await Task.WhenAll(
                client.PatchAsJsonAsync(new Uri($"{Root}/{id:D}?locale=en", UriKind.Relative), new { alt = $"Boats {round}" }, Token),
                client.PatchAsJsonAsync(new Uri($"{Root}/{id:D}?locale=fr", UriKind.Relative), new { alt = $"Bateaux {round}" }, Token));
            foreach (var save in saves)
            {
                await JsonAsync(save);
                save.Dispose();
            }

            (await GetAsync(client, $"{Root}/{id:D}?locale=en"))["alt"]!.GetValue<string>().ShouldBe($"Boats {round}");
            (await GetAsync(client, $"{Root}/{id:D}?locale=fr"))["alt"]!.GetValue<string>().ShouldBe($"Bateaux {round}");
        }
    }

    // A move to the folder the asset is in writes nothing, so it answers the asset and version as stored.
    [Fact]
    public async Task AMoveToTheFolderTheAssetIsInAnswersTheAssetAsStored()
    {
        using var client = await media.AdminAsync();
        var id = await AssetAsync(client);
        var before = await GetAsync(client, $"{Root}/{id:D}");

        using var moved = await client.PostAsJsonAsync(new Uri($"{Root}/{id:D}/move", UriKind.Relative), new { folderId = (Guid?)null }, Token);
        var answer = await JsonAsync(moved);

        moved.Headers.ETag!.Tag.ShouldBe("\"1\"");
        answer["updatedAt"]!.GetValue<DateTimeOffset>().ShouldBe(before["updatedAt"]!.GetValue<DateTimeOffset>());
        var after = await GetAsync(client, $"{Root}/{id:D}");
        after["updatedAt"]!.GetValue<DateTimeOffset>().ShouldBe(before["updatedAt"]!.GetValue<DateTimeOffset>());
        (await ReadAsync(client, id)).ShouldBe("\"1\"");
    }

    private static async Task<Guid> AssetAsync(HttpClient client) =>
        Id((await UploadedAsync(client, null, ("photo.png", Files.Png(4, 4)))).Single()!);

    private static async Task<string> ReadAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token);
        await JsonAsync(response);
        return response.Headers.ETag!.Tag;
    }

    private static async Task<HttpResponseMessage> WriteAsync(HttpClient client, string write, Guid id, string? ifMatch, string title = "Mine")
    {
        HttpRequestMessage request = write switch
        {
            "patch" => new(HttpMethod.Patch, new Uri($"{Root}/{id:D}", UriKind.Relative)) { Content = JsonContent.Create(new { title }) },
            "move" => new(HttpMethod.Post, new Uri($"{Root}/{id:D}/move", UriKind.Relative)) { Content = JsonContent.Create(new { folderId = await FolderAsync(client, Unique("moved")) }) },
            "replace" => new(HttpMethod.Post, new Uri($"{Root}/{id:D}/replace", UriKind.Relative)) { Content = File("other.png", Files.Png(6, 6)) },
            "delete" => new(HttpMethod.Delete, new Uri($"{Root}/{id:D}", UriKind.Relative)),
            _ => throw new ArgumentOutOfRangeException(nameof(write)),
        };

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch).ShouldBeTrue();
        }

        using (request)
        {
            return await client.SendAsync(request, Token);
        }
    }

    private static async Task<HttpResponseMessage> SendDeleteAsync(HttpClient client, Guid id, string ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"{Root}/{id:D}?force=true", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch).ShouldBeTrue();
        return await client.SendAsync(request, Token);
    }

    private static MultipartFormDataContent File(string name, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", name } };
    }
}
