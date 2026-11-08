using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// What one call of the content service sends to the database, counted as RoundTrips counts it. The references of an
// entry are checked per target type, so the count of a publish, a schedule, a diff or a version restore does not grow
// with them.
public sealed class ContentRoundTripTests : IDisposable
{
    private static readonly string[] Cards = ["card"];

    private readonly RoundTrips _trips = new();
    private readonly ServiceDatabase _database;

    public ContentRoundTripTests()
    {
        _database = new ServiceDatabase(cms => cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(_trips)), Model());
    }

    public void Dispose() => _database.Dispose();

    // A non-localized type with relations at the top level and in blocks, a slug and rich text.
    private static SchemaModel Model() => SampleSchema.NewBuilder()
        .AddBlock("card", block => block
            .Field("heading", "text")
            .Field("who", "relation", field => field.Relation("author")))
        .AddCollection("author", type => type.Field("name", "text"))
        .AddCollection("tag", type => type.Scope(ContentScope.Global).Field("name", "text"))
        .AddCollection("post", type => type
            .PublishPolicy(UnpublishedReferencePolicy.Block)
            .Field("title", "text", field => field.Required())
            .Field("slug", "slug", field => field.Unique().Setting(FieldSettings.Source, "title"))
            .Field("body", "richText")
            .Field("views", "integer")
            .Field("author", "relation", field => field.Relation("author"))
            .Field("authors", "relation", field => field.Multiple().Relation("author"))
            .Field("tags", "relation", field => field.Multiple().Relation("tag"))
            .Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, Cards)))
        .Build();

    [Fact]
    public async Task ATypicalUpdateMakesEighteenRoundTripsAndCopiesTheEntryTwice()
    {
        var id = await PostAsync(Editor, await AuthorsAsync(Editor, 2));
        var read = (await _database.GetAsync(Editor, id, type: "post"))!;
        read["title"] = "Post two";
        read["views"] = 2L;

        var copies = await CopiesAsync(() => _trips.CountAsync(() => _database.UpdateAsync(Editor, id, read, 1, "post")));

        // Three to resolve the locale and the grants, begin, ten in the transaction, commit, and three to read the answer.
        _trips.Count.ShouldBeLessThanOrEqualTo(18, string.Join('\n', _trips.Sent));
        copies.ShouldBeLessThanOrEqualTo(2);
        (await _database.GetAsync(Editor, id, type: "post"))!["title"].ShouldBe("Post two");
    }

    [Fact]
    public async Task ACreateThatGivesItsBlocksIdsCopiesTheEntryTwice()
    {
        var author = (await AuthorsAsync(Editor, 1))[0];
        ContentDocument entry = new() { ["title"] = "Post", ["cards"] = new JsonArray(new JsonObject { ["type"] = "card", ["who"] = author.ToString() }) };

        ContentDocument? created = null;
        var copies = await CopiesAsync(async () => created = await _database.CreateAsync(Editor, entry, "post"));

        copies.ShouldBeLessThanOrEqualTo(2);
        var block = (string)((JsonArray)created!["cards"]!)[0]!["id"]!;
        _database.Versions(IdOf(created)).ShouldHaveSingleItem().Snapshot.ShouldContain($"\"id\":\"{block}\"");
    }

    [Fact]
    public async Task ListingVersionsLeavesTheSnapshotsInTheDatabase()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Post" }, "post"));
        await _database.UpdateAsync(Editor, id, new() { ["title"] = "Post two" }, 1, "post");

        var versions = await _trips.CountAsync(() => _database.VersionsAsync(Editor, id, type: "post"));

        versions.Select(version => version.Number).ShouldBe([2, 1]);
        var read = _trips.Sent.Where(sql => sql.Contains("FROM \"cms_version\"", StringComparison.Ordinal)).ShouldHaveSingleItem();
        read.ShouldContain("\"status_after\"");
        read.ShouldNotContain("snapshot");
    }

    private static async Task<int> CopiesAsync<T>(Func<Task<T>> work)
    {
        var copies = new StrongBox<int>();
        EntryJson.FieldsCalls.Value = copies;
        try
        {
            await work();
        }
        finally
        {
            EntryJson.FieldsCalls.Value = null;
        }

        return copies.Value;
    }

    [Fact]
    public async Task ReferenceChecksAreBoundedWhateverTheNumberOfReferences()
    {
        var few = await CountsAsync(Editor, 2);
        var many = await CountsAsync(Editor, 20);

        many.ShouldBe(few);
    }

    [Fact]
    public async Task ReferenceChecksUnderARowRuleAreBoundedWhateverTheNumberOfReferences()
    {
        var role = _database.Database.AddRoleForTest(
            "Own authors",
            ("content.post.*", null),
            ("content.tag.read", null),
            ("content.author.create", null),
            ("content.author.publish", null),
            ("content.author.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }));
        var principal = WithRole(role, Guid.CreateVersion7());

        var few = await CountsAsync(principal, 2);
        var many = await CountsAsync(principal, 20);

        many.ShouldBe(few);
    }

    // The round trips of a publish, a schedule, a diff and a version restore of a post with this many references to authors.
    private async Task<(int Publish, int Schedule, int Diff, int Restore)> CountsAsync(Principal principal, int references)
    {
        var id = await PostAsync(principal, await AuthorsAsync(principal, references));
        await _database.PatchAsync(principal, id, new() { ["views"] = 2L }, 1, "post");

        await _trips.CountAsync(() => _database.PublishAsync(principal, id, type: "post"));
        var publish = _trips.Count;

        await _trips.CountAsync(() => _database.AsAsync(principal, services => services.GetRequiredService<IContentService>().ScheduleAsync("post", id, new ScheduleRequest(ScheduleAction.Publish, DateTime.UtcNow.AddDays(1)), null, Token)));
        var schedule = _trips.Count;

        await _trips.CountAsync(() => _database.DiffAsync(principal, id, 1, 2, type: "post"));
        var diff = _trips.Count;

        await _trips.CountAsync(() => _database.RestoreVersionAsync(principal, id, 1, type: "post"));
        return (publish, schedule, diff, _trips.Count);
    }

    private async Task<List<Guid>> AuthorsAsync(Principal principal, int count)
    {
        List<Guid> ids = [];
        for (var index = 0; index < count; index++)
        {
            var id = IdOf(await _database.CreateAsync(principal, new() { ["name"] = $"Author {index}" }, "author"));
            await _database.PublishAsync(principal, id, type: "author");
            ids.Add(id);
        }

        return ids;
    }

    private async Task<Guid> PostAsync(Principal principal, List<Guid> authors)
    {
        var tag = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Tag" }, "tag"));
        await _database.PublishAsync(Editor, tag, type: "tag");

        JsonArray cards = [.. authors.Select(author => (JsonNode)new JsonObject { ["type"] = "card", ["heading"] = "Card", ["who"] = author.ToString() })];
        return IdOf(await _database.CreateAsync(principal, new()
        {
            ["title"] = $"Post {authors.Count}",
            ["views"] = 1L,
            ["author"] = authors[0],
            ["authors"] = authors.ToArray(),
            ["tags"] = new[] { tag },
            ["cards"] = cards,
        }, "post"));
    }
}
