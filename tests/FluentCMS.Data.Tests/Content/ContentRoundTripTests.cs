using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Transfer;
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
            .PublishPolicy(new PublishPolicyOptions { UnpublishedReferences = UnpublishedReferencePolicy.Block })
            .Field("title", "text", field => field.Required())
            .Field("slug", "slug", field => field.Unique().Setting(FieldSettings.Source, "title"))
            .Field("body", "richText")
            .Field("views", "integer")
            .Field("author", "relation", field => field.Relation("author"))
            .Field("authors", "relation", field => field.Multiple().Relation("author"))
            .Field("tags", "relation", field => field.Multiple().Relation("tag"))
            .Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, Cards)))
        .AddCollection("story", type => type
            .Localized()
            .PublishPolicy(new PublishPolicyOptions { UnpublishedReferences = UnpublishedReferencePolicy.Block })
            .Field("title", "text", field => field.Required().Localizable())
            .Field("slug", "slug", field => field.Localizable().Unique().Setting(FieldSettings.Source, "title"))
            .Field("body", "richText", field => field.Localizable())
            .Field("views", "integer")
            .Field("author", "relation", field => field.Relation("author"))
            .Field("authors", "relation", field => field.Multiple().Relation("author"))
            .Field("tags", "relation", field => field.Multiple().Relation("tag"))
            .Field("cards", "blocks", field => field.Setting(FieldSettings.Blocks, Cards)))
        .Build();

    [Fact]
    public async Task ATypicalUpdateMakesTwelveRoundTripsAndCopiesTheEntryTwice()
    {
        var id = await PostAsync(Editor, await AuthorsAsync(Editor, 2));
        var read = (await _database.GetAsync(Editor, id, type: "post"))!;
        read["title"] = "Post two";
        read["views"] = 2L;

        ContentDocument? answer = null;
        var copies = await CopiesAsync(async () => answer = await _trips.CountAsync(() => _database.UpdateAsync(Editor, id, read, 1, "post")));

        // One for the grants, begin, eight in the transaction, commit, and the row of the answer. The read the principal
        // is shown is made of the draft rows and references just read and one readable-targets query, and the answer
        // reuses the references written and the targets found readable. A server's transaction also sets its lock timeout,
        // which SQL Server sets back before the commit (#3573).
        _trips.Count.ShouldBeLessThanOrEqualTo(12 + LockTimeout, string.Join('\n', _trips.Sent));
        copies.ShouldBeLessThanOrEqualTo(2);
        answer!["title"].ShouldBe("Post two");
        Json(answer).ShouldBe(Json(await _database.GetAsync(Editor, id, type: "post")));
    }

    // The statements a server's transaction adds to set its lock timeout, and on SQL Server to set it back (#3573).
    private static int LockTimeout => TestProvider.IsSqlite ? 0 : TestProvider.IsPostgreSql ? 1 : 2;

    // SQL Server has no release of a savepoint, so a write nested in another's sends one statement less there.
    private static int Unreleased(int savepoints) => TestProvider.IsSqlServer ? savepoints : 0;

    [Fact]
    public async Task APublishOfOneOfTwoLocalesMakesThirtyEightRoundTrips()
    {
        var id = await StoryAsync(await AuthorsAsync(Editor, 2));
        await _database.PublishAsync(Editor, id, type: "story", operationLocale: "fr");

        var result = await _trips.CountAsync(() => _database.PublishAsync(Editor, id, type: "story"));

        // One for the grants, begin, 32 in the transaction, commit, and three for the answer (its rows, references and readable
        // targets). In the transaction: the entry's rows; the draft and the published state, three each (the shared row, the
        // locale's row, the references); the [Unique] check; three for the references (the state of the authors and of the
        // tag, and the authors in the site); the draft read again (#3536); two holds, the own row at the version read and the
        // French one (#3524); seven for the copy (the published shared and locale rows read and deleted, the published
        // references deleted, the draft references read, one save); the status of the row and of its version; and seven to
        // decide the status of French (the entry's rows, its draft and published state). This host stages no event.
        result.Entry["status"].ShouldBe(EntryStatus.Published);
        _trips.Count.ShouldBeLessThanOrEqualTo(38 + LockTimeout, string.Join('\n', _trips.Sent));
    }

    [Fact]
    public async Task AScheduledPublishOfOneOfTwoLocalesMakesSixtyThreeRoundTrips()
    {
        var id = await StoryAsync(await AuthorsAsync(Editor, 2));
        await _database.PublishAsync(Editor, id, type: "story", operationLocale: "fr");
        await _database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().ScheduleAsync("story", id, ScheduleRequest.At(ScheduleAction.Publish, DateTimeOffset.UtcNow.AddDays(1)), null, Token));
        var schedule = _database.Read(context => context.Set<ScheduleRow>().AsNoTracking().Single(row => row.DocumentId == id));

        var outcome = await _trips.CountAsync(() => RunScheduledAsync(schedule, Editor));

        // The schedule, begin, 60 in the transaction and commit. In it: the entry's rows, two holds, and the rows and the
        // schedule read again under them (#3524, #3534); the type and trash of the row; the entry's rows and the draft for
        // the scheduled version and shared hash; the claim; the mirror read and cleared; the scheduler's grants; ten for its
        // checks (the entry's rows, the draft and the published state, three for the references); the publish in a
        // savepoint with its release, 31 as above less the grants, the transaction, the answer and one reference check, which
        // the system principal does not make, with its own two holds of the rows already held; and its answer, three, unused.
        outcome.ShouldBe(ScheduleOutcome.Executed);
        _trips.Count.ShouldBeLessThanOrEqualTo(63 + LockTimeout - Unreleased(1), string.Join('\n', _trips.Sent));
    }

    [Fact]
    public async Task AnImportOfAnEntryInTwoLocalesMakesAHundredAndEightRoundTrips()
    {
        var id = await StoryAsync(await AuthorsAsync(Editor, 2));
        await _database.PublishAsync(Editor, id, type: "story", operationLocale: "fr");
        await _database.PublishAsync(Editor, id, type: "story");
        var package = await PackageAsync(id);

        var outcome = await _trips.CountAsync(() => _database.AsAsync(
            Principal.System,
            services =>
            {
                services.GetRequiredService<TransferImportMode>().Active = true;
                return services.GetRequiredService<ContentService>().ImportAsync("story", package, overwrite: true, authorsFromImporter: false, Token);
            },
            origin: Origin.Import));

        // The entry's rows, begin, 105 in the transaction and commit. In it: the entry's rows, two holds and the rows read
        // again under them (#3578); for each locale 49: a save of 15 (the entry's rows, the draft, the [Unique] check, the
        // swap, the references deleted, one save, and seven to decide the status of the other locale), the publish in a
        // savepoint with its release, 29 as above less the grants, the transaction, the answer and the reference checks,
        // which an import does not make, with its own two holds of the rows already held, and its answer, three, unused;
        // then the rows read and the system fields of each locale settled, three.
        outcome.ShouldBe(TransferOutcome.Updated);
        _trips.Count.ShouldBeLessThanOrEqualTo(108 + LockTimeout - Unreleased(2), string.Join('\n', _trips.Sent));
    }

    // The job body under the operation the runner gives it, as the system acting for the principal that scheduled it.
    private async Task<ScheduleOutcome> RunScheduledAsync(ScheduleRow schedule, Principal scheduler)
    {
        using var scope = _database.Database.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = new OperationContext(Principal.System, Origin.Job, SiteA, "en", null, "test", DateTimeOffset.UtcNow)
        {
            Actor = new Principal(scheduler.Kind, scheduler.Id, scheduler.Name),
        };

        try
        {
            return await scope.ServiceProvider.GetRequiredService<ContentService>().RunScheduledAsync(schedule.Id, schedule.JobId!.Value, ScheduleAction.Publish, lastAttempt: false, Token);
        }
        finally
        {
            accessor.Current = null;
        }
    }

    // The entry as an export writes it, with a new title in each locale and a later time, so that the import writes both.
    private async Task<TransferEntry> PackageAsync(Guid id)
    {
        var later = DateTimeOffset.UtcNow.AddHours(1);
        var exported = await _database.AsAsync(Principal.System, async services =>
        {
            var type = services.GetRequiredService<SchemaModel>().FindContentType("story")!;
            await foreach (var entry in services.GetRequiredService<EntryExport>().ReadAsync(type, SiteA, publishedOnly: false, Token))
            {
                if (entry.Id == id)
                {
                    return entry;
                }
            }

            throw new InvalidOperationException("The story was not exported.");
        });

        return exported with
        {
            Locales = [.. exported.Locales.Select(locale =>
            {
                var fields = (JsonObject)locale.Published!.DeepClone();
                fields["title"] = $"{fields["title"]} two";
                return locale with { UpdatedAt = later, Draft = fields, Published = (JsonObject)fields.DeepClone() };
            })],
        };
    }

    [Fact]
    public async Task AnUpdateThatAddsATargetAnswersWhatAReadShows()
    {
        var authors = await AuthorsAsync(Editor, 3);
        var id = await PostAsync(Editor, authors[..2]);
        var read = (await _database.GetAsync(Editor, id, type: "post"))!;
        read["authors"] = authors.ToArray();

        var answer = await _database.UpdateAsync(Editor, id, read, 1, "post");

        Json(answer).ShouldBe(Json(await _database.GetAsync(Editor, id, type: "post")));
        Json(answer)!.ShouldContain(authors[2].ToString());
    }

    [Fact]
    public async Task AnUpdateUnderARowRuleAnswersWhatAReadShows()
    {
        var role = _database.Database.AddRoleForTest(
            "Own posts",
            ("content.post.*", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }),
            ("content.tag.read", null),
            ("content.author.*", null));
        var principal = WithRole(role, Guid.CreateVersion7());
        var id = await PostAsync(principal, await AuthorsAsync(principal, 2));
        var read = (await _database.GetAsync(principal, id, type: "post"))!;
        read["title"] = "Post two";

        var answer = await _database.UpdateAsync(principal, id, read, 1, "post");

        answer["title"].ShouldBe("Post two");
        Json(answer).ShouldBe(Json(await _database.GetAsync(principal, id, type: "post")));
    }

    private static string? Json(ContentDocument? entry) => EntryJson.ToNode(entry)?.ToJsonString();

    [Fact]
    public async Task ACreateThatGivesItsBlocksIdsCopiesTheEntryTwice()
    {
        var author = (await AuthorsAsync(Editor, 1))[0];
        ContentDocument entry = new() { ["title"] = "Post", ["cards"] = new JsonArray(new JsonObject { ["type"] = "card", ["who"] = author.ToString() }) };

        ContentDocument? created = null;
        var copies = await CopiesAsync(async () => created = await _database.CreateAsync(Editor, entry, "post"));

        copies.ShouldBeLessThanOrEqualTo(2);
        var block = (string)((JsonArray)created!["cards"]!)[0]!["id"]!;
        StoredJson.Parse(_database.Versions(IdOf(created)).ShouldHaveSingleItem().Snapshot)["cards"]![0]!["id"]!.GetValue<string>().ShouldBe(block);
    }

    [Fact]
    public async Task ListingVersionsLeavesTheSnapshotsInTheDatabase()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Post" }, "post"));
        await _database.UpdateAsync(Editor, id, new() { ["title"] = "Post two" }, 1, "post");

        var versions = await _trips.CountAsync(() => _database.VersionsAsync(Editor, id, type: "post"));

        versions.Select(version => version.Number).ShouldBe([2, 1]);
        var read = _trips.Sent.Where(sql => sql.Contains($"FROM {TestProvider.Quote("cms_version")}", StringComparison.Ordinal)).ShouldHaveSingleItem();
        read.ShouldContain(TestProvider.Quote("status_after"));
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

        await _trips.CountAsync(() => _database.AsAsync(principal, services => services.GetRequiredService<IContentService>().ScheduleAsync("post", id, ScheduleRequest.At(ScheduleAction.Publish, DateTimeOffset.UtcNow.AddDays(1)), null, Token)));
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

    // A story like the post of PostAsync in English, and in French with its own title.
    private async Task<Guid> StoryAsync(List<Guid> authors)
    {
        var tag = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Tag" }, "tag"));
        await _database.PublishAsync(Editor, tag, type: "tag");

        JsonArray cards = [.. authors.Select(author => (JsonNode)new JsonObject { ["type"] = "card", ["heading"] = "Card", ["who"] = author.ToString() })];
        ContentDocument story = new()
        {
            ["title"] = $"Story {authors.Count}",
            ["views"] = 1L,
            ["author"] = authors[0],
            ["authors"] = authors.ToArray(),
            ["tags"] = new[] { tag },
            ["cards"] = cards,
        };
        var id = IdOf(await _database.CreateAsync(Editor, story, "story"));
        var french = (await _database.GetAsync(Editor, id, type: "story"))!;
        french["title"] = $"Histoire {authors.Count}";
        french.Remove("slug");
        await _database.UpdateAsync(Editor, id, french, 0, "story", locale: "fr");
        return id;
    }
}
