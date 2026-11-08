using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class ContentServiceWriteTests : IDisposable
{
    private const string Body = """{"content":[{"content":[{"text":"Hi","type":"text"}],"type":"paragraph"}],"type":"doc"}""";
    private const string CanonicalBody = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hi"}]}]}""";
    private static readonly string[] Clip = ["news", "video"];
    private static readonly string[] News = ["news"];

    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    [Fact]
    public async Task CreateWritesEveryTableAndReturnsTheSystemFields()
    {
        var author = await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author");

        var entry = await _database.CreateAsync(Editor, new()
        {
            ["title"] = "Hello World",
            ["views"] = 3,
            ["author"] = IdOf(author),
            ["body"] = Body,
        });

        entry["version"].ShouldBe(1);
        entry["status"].ShouldBe(EntryStatus.Draft);
        entry["locale"].ShouldBe("en");
        entry["title"].ShouldBe("Hello World");
        entry["slug"].ShouldBe("hello-world");
        entry["category"].ShouldBe("news");
        entry.ContainsKey("createdBy").ShouldBeFalse();

        var id = IdOf(entry);
        var version = _database.Versions(id).ShouldHaveSingleItem();
        version.Number.ShouldBe(1);
        version.StatusAfter.ShouldBe(EntryStatus.Draft);
        version.AuthorId.ShouldBe(Editor.Id);
        version.Origin.ShouldBe(Origin.Api);
        version.ClientId.ShouldBe("client");
        JsonNode.Parse(version.Snapshot)!["title"]!.GetValue<string>().ShouldBe("Hello World");

        _database.Row("cms_article", id)["views"].ShouldBe(3L);
        _database.Row("cms_article_i18n", id, "en")["title"].ShouldBe("Hello World");
        _database.References(id).ShouldHaveSingleItem().TargetId.ShouldBe(IdOf(author));
    }

    [Fact]
    public async Task RichTextIsStoredInItsCanonicalForm()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["body"] = Body }));

        _database.Row("cms_article_i18n", id, "en")["body"].ShouldBeJson(CanonicalBody);
        (await _database.GetAsync(Editor, id))!["body"].ShouldBeOfType<RichText>().Json.ShouldBe(CanonicalBody);
    }

    [Fact]
    public async Task InvalidRichTextIsRefused()
    {
        var refused = await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["body"] = """{"type":"doc","content":[{"type":"nothing"}]}""" }));

        refused.Errors[0].Path.ShouldBe("body");
        refused.Errors[0].Code.ShouldBe(ValidationErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task ADraftWithMissingRequiredTextWritesThePlaceholder()
    {
        var entry = await _database.CreateAsync(Editor, []);

        var row = _database.Row("cms_article_i18n", IdOf(entry), "en");
        row["title"].ShouldBe("");
        row["slug"].ShouldBe("");
        row["body"].ShouldBeJson(RichText.Empty.Json);
        row["subtitle"].ShouldBeNull();

        var shared = _database.Row("cms_article", IdOf(entry));
        shared["labels"].ShouldBe("[]");
        shared["sections"].ShouldBe("[]");
        shared["views"].ShouldBeNull();

        entry["title"].ShouldBe("");
    }

    [Fact]
    public async Task ATitleInAnotherScriptGivesASlugThatIsStoredAsItIsAndPublishes()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Привет, мир"));
        var id = IdOf(entry);

        entry["slug"].ShouldBe("привет-мир");
        _database.Row("cms_article_i18n", id, "en")["slug"].ShouldBe("привет-мир");

        var read = (await _database.GetAsync(Editor, id))!;
        read["title"] = "日本語";
        (await _database.UpdateAsync(Editor, id, read, 1))["slug"].ShouldBe("привет-мир");
        (await _database.PublishAsync(Editor, id)).Entry["slug"].ShouldBe("привет-мир");
    }

    [Fact]
    public async Task ATitleWithNoLetterOrDigitIsRefusedAtSaveForARequiredSlug()
    {
        var schema = SampleSchema.NewBuilder()
            .AddCollection("note", type => type
                .Field("title", "text")
                .Field("slug", "slug", field => field.Required().Setting(FieldSettings.Source, "title")))
            .Build();
        using var database = new ServiceDatabase(schema: schema);

        var refused = await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(Editor, Titled("!!! ???"), "note"));

        var error = refused.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("slug");
        error.Code.ShouldBe(ValidationErrorCodes.EmptySlug);

        (await database.CreateAsync(Editor, new() { ["title"] = "!!! ???", ["slug"] = "questions" }, "note"))["slug"].ShouldBe("questions");
        (await database.CreateAsync(Editor, Titled("   "), "note"))["slug"].ShouldBe("");
    }

    [Fact]
    public async Task AnOptionalSlugThatTheTitleCannotGiveStaysEmpty()
    {
        (await _database.CreateAsync(Editor, Titled("!!! ???")))["slug"].ShouldBe("");
    }

    [Fact]
    public async Task TwoDraftsWithThePlaceholderDoNotClash()
    {
        await _database.CreateAsync(Editor, []);

        await Should.NotThrowAsync(() => _database.CreateAsync(Editor, []));
    }

    [Fact]
    public async Task BlockDatesAreStoredInTheFormsTheFiltersCompare()
    {
        var entry = await _database.CreateAsync(Editor, new()
        {
            ["title"] = "Hello",
            ["sections"] = JsonNode.Parse("""[{"type":"hero","at":"2026-11-02T10:00:00+02:00","on":"2026-11-02","note":{"content":[{"type":"paragraph"}],"type":"doc"}}]"""),
        });

        var stored = JsonNode.Parse((string)_database.Row("cms_article", IdOf(entry))["sections"])!;
        stored[0]!["at"]!.GetValue<string>().ShouldBe("2026-11-02T08:00:00Z");
        stored[0]!["on"]!.GetValue<string>().ShouldBe("2026-11-02");
        stored[0]!["note"].ShouldBeJson(RichText.Empty.Json);
    }

    [Fact]
    public async Task ABlockOutsideTheFieldIsRefused()
    {
        var refused = await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["sections"] = JsonNode.Parse("""[{"type":"other"}]""") }));

        refused.Errors.ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCodes.NotAnAllowedOption);
    }

    [Fact]
    public async Task AValueInsideAComponentOrBlockIsValidated()
    {
        var refused = await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new()
        {
            ["title"] = "Hello",
            ["seo"] = new ContentDocument { ["metaTitle"] = new string('x', 11) },
            ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"much too long a heading"}]"""),
        }));

        refused.Errors.Select(error => error.Path).ShouldBe(["seo.metaTitle", "sections[0].heading"], ignoreOrder: true);
    }

    [Fact]
    public async Task AValidationFailureWritesNothing()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Hello"));
        var before = _database.Footprint();

        await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "Other", ["views"] = 5000 }));
        await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(Editor, IdOf(entry), new() { ["views"] = 5000 }, 1));

        _database.Footprint().ShouldBe(before);
        (await _database.GetAsync(Editor, IdOf(entry)))!["version"].ShouldBe(1);
    }

    [Fact]
    public async Task AnUnknownFieldIsRefusedEvenForTheSystemPrincipal()
    {
        var refused = await Should.ThrowAsync<ValidationException>(() => _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().CreateAsync("article", new() { ["nope"] = 1 }, Token),
            origin: Origin.Seed));

        refused.Errors.ShouldHaveSingleItem().Path.ShouldBe("nope");
    }

    [Fact]
    public async Task AStaleVersionFailsWithTheCurrentOneAttached()
    {
        var entry = await _database.CreateAsync(Editor, Titled("One"));
        var updated = await _database.UpdateAsync(Editor, IdOf(entry), Titled("Two"), 1);
        updated["version"].ShouldBe(2);

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => _database.UpdateAsync(Editor, IdOf(entry), Titled("Three"), 1));

        conflict.ExpectedVersion.ShouldBe(1);
        conflict.CurrentVersion.ShouldBe(2);
        conflict.Current!["title"].ShouldBe("Two");
        conflict.Current["version"].ShouldBe(2);
        (await _database.GetAsync(Editor, IdOf(entry)))!["title"].ShouldBe("Two");
        _database.Versions(IdOf(entry)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task AVersionConflictOmitsWhatThePrincipalMayNotRead()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "One", ["views"] = 5 });
        await _database.UpdateAsync(Editor, IdOf(entry), new() { ["title"] = "Two", ["views"] = 5 }, 1);
        var role = _database.Database.AddRoleForTest("Limited", ("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.article.update", null));

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => _database.PatchAsync(WithRole(role), IdOf(entry), Titled("Three"), 1));

        conflict.Current!["title"].ShouldBe("Two");
        conflict.Current.ContainsKey("views").ShouldBeFalse();
    }

    [Fact]
    public async Task TwoConcurrentUpdatesInTwoContextsHaveOneWinner()
    {
        for (var round = 0; round < 5; round++)
        {
            var entry = await _database.CreateAsync(Editor, Titled($"Start {round}"));
            var id = IdOf(entry);

            var results = await Task.WhenAll(
                Task.Run(() => Attempt($"Left {round}")),
                Task.Run(() => Attempt($"Right {round}")));

            results.Count(result => result is null).ShouldBe(1);
            results.Single(result => result is not null)!.CurrentVersion.ShouldBe(2);
            _database.Versions(id).Count.ShouldBe(2);

            async Task<VersionConflictException?> Attempt(string title)
            {
                try
                {
                    await _database.UpdateAsync(Editor, id, Titled(title), 1);
                    return null;
                }
                catch (VersionConflictException conflict)
                {
                    return conflict;
                }
            }
        }
    }

    [Fact]
    public async Task TheRowsOfAnEntryComeOldestFirstWhateverTheOrderOfTheirIds()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("Hello")));
        await _database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        var created = _database.Read(context => context.Documents.Single(document => document.DocumentId == id && document.Locale == "en")).CreatedAt;

        // One of the two orders goes against the order of the ids, whichever that is.
        async Task<IEnumerable<string>> OldestFirstWhenAsync(string younger)
        {
            _database.Read(context => context.Documents
                .Where(document => document.DocumentId == id)
                .ExecuteUpdate(set => set.SetProperty(document => document.CreatedAt, document => document.Locale == younger ? created.AddDays(1) : created)));

            using var scope = _database.Database.NewScope();
            var rows = await scope.ServiceProvider.GetRequiredService<EntryStore>().DocumentsAsync(ServiceDatabase.Schema.FindContentType("article")!, id, SiteA, trashed: false, Token);
            return rows.Select(row => row.Locale);
        }

        (await OldestFirstWhenAsync("en")).ShouldBe(["fr", "en"]);
        (await OldestFirstWhenAsync("fr")).ShouldBe(["en", "fr"]);
    }

    [Fact]
    public async Task TheVersionSwapLetsOnlyOneOfTwoContextsAdvance()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Start"));
        var id = IdOf(entry);

        using var left = _database.Database.NewScope();
        using var right = _database.Database.NewScope();
        var leftStore = left.ServiceProvider.GetRequiredService<EntryStore>();
        var rightStore = right.ServiceProvider.GetRequiredService<EntryStore>();

        (await leftStore.AdvanceVersionAsync(id, "en", 1, EntryStatus.Draft, DateTimeOffset.UtcNow, Editor.Id, Token)).ShouldBeTrue();
        (await rightStore.AdvanceVersionAsync(id, "en", 1, EntryStatus.Draft, DateTimeOffset.UtcNow, Editor.Id, Token)).ShouldBeFalse();
        (await rightStore.DraftVersionAsync(id, "en", Token)).ShouldBe(2);
    }

    [Fact]
    public async Task AMergePatchRemovesAFieldAndLeavesTheRest()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "Sub", ["views"] = 4 });

        var patched = await _database.PatchAsync(Editor, IdOf(entry), new() { ["subtitle"] = null, ["views"] = 5 }, 1);

        patched["subtitle"].ShouldBeNull();
        patched["views"].ShouldBe(5L);
        patched["title"].ShouldBe("Hello");
        patched["version"].ShouldBe(2);
    }

    [Fact]
    public async Task AMergePatchMergesIntoAComponentAndAJsonField()
    {
        var entry = await _database.CreateAsync(Editor, new()
        {
            ["title"] = "Hello",
            ["seo"] = new ContentDocument { ["metaTitle"] = "Keep" },
            ["metadata"] = JsonNode.Parse("""{"a":1,"b":2,"c":{"d":3,"e":4}}"""),
        });

        var patched = await _database.PatchAsync(Editor, IdOf(entry), new()
        {
            ["metadata"] = JsonNode.Parse("""{"a":null,"c":{"d":9}}"""),
        }, 1);

        ((ContentDocument)patched["seo"]!)["metaTitle"].ShouldBe("Keep");
        JsonNode.DeepEquals((JsonNode)patched["metadata"]!, JsonNode.Parse("""{"b":2,"c":{"d":9,"e":4}}""")).ShouldBeTrue();

        var cleared = await _database.PatchAsync(Editor, IdOf(entry), new() { ["seo"] = JsonNode.Parse("""{"metaTitle":null}""") }, 2);
        ((ContentDocument)cleared["seo"]!)["metaTitle"].ShouldBeNull();
    }

    [Fact]
    public async Task AFullUpdateClearsWhatItLeavesOutAndKeepsWhatThePrincipalCannotSee()
    {
        var entry = await _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().CreateAsync("article", new()
            {
                ["title"] = "Hello",
                ["subtitle"] = "Sub",
                ["secret"] = "kept",
                ["views"] = 5,
                ["seo"] = new ContentDocument { ["metaTitle"] = "Meta", ["internal"] = "inner" },
                ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"Hi","internal":"deep"}]"""),
            }, Token),
            origin: Origin.Seed);
        var id = IdOf(entry);

        await _database.UpdateAsync(Editor, id, new()
        {
            ["title"] = "Changed",
            ["seo"] = new ContentDocument { ["metaTitle"] = "New" },
            ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"Yo"}]"""),
        }, 1);

        var shared = _database.Row("cms_article", id);
        shared["secret"].ShouldBe("kept");
        shared["views"].ShouldBeNull();
        shared["seo_internal"].ShouldBe("inner");
        shared["seo_meta_title"].ShouldBe("New");
        var block = JsonNode.Parse((string)shared["sections"])![0]!;
        block["heading"]!.GetValue<string>().ShouldBe("Yo");
        block["internal"]!.GetValue<string>().ShouldBe("deep");

        _database.Row("cms_article_i18n", id, "en")["subtitle"].ShouldBeNull();
    }

    [Fact]
    public async Task WhatARoundTripSendsBackIsNoChange()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var tag = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Net" }, "tag"));
        var entry = await _database.CreateAsync(Editor, new()
        {
            ["title"] = "Hello",
            ["author"] = author,
            ["tags"] = new[] { tag },
            ["hero"] = Guid.CreateVersion7(),
            ["labels"] = new List<string> { "net" },
            ["publishedOn"] = "2026-11-02T10:00:00+02:00",
            ["issued"] = "2026-11-02",
            ["price"] = 9.5m,
            ["seo"] = new ContentDocument { ["metaTitle"] = "Meta" },
            ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"Hi","image":"0192f2a4-3333-7000-8000-000000000003"}]"""),
        });

        var read = (await _database.GetAsync(Editor, IdOf(entry)))!;
        var saved = await _database.UpdateAsync(Editor, IdOf(entry), read, 1);

        foreach (var name in new[] { "title", "author", "tags", "hero", "labels", "publishedOn", "issued", "price", "seo", "sections", "body" })
        {
            EntryJson.Same(saved[name], read[name]).ShouldBeTrue(name);
        }

        saved["version"].ShouldBe(2);
    }

    [Fact]
    public async Task SystemFieldsSentByAClientAreIgnoredOrRefused()
    {
        var entry = await _database.CreateAsync(Editor, new()
        {
            ["title"] = "Hello",
            ["id"] = Guid.Empty,
            ["version"] = 99,
            ["status"] = "published",
            ["createdAt"] = "2000-01-01T00:00:00Z",
        });

        IdOf(entry).ShouldNotBe(Guid.Empty);
        entry["version"].ShouldBe(1);
        entry["status"].ShouldBe(EntryStatus.Draft);
        ((DateTimeOffset)entry["createdAt"]!).Year.ShouldNotBe(2000);

        (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["createdBy"] = Guid.CreateVersion7() })))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable);
        (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(Editor, IdOf(entry), new() { ["updatedBy"] = Guid.CreateVersion7() }, 1)))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable);
    }

    public static TheoryData<string, object> UnwritableShapes()
    {
        const string Json = """{"internal":"x"}""";
        return new TheoryData<string, object>
        {
            { "seo", new ContentDocument { ["internal"] = "x" } },
            { "seo", new Dictionary<string, object?> { ["internal"] = "x" } },
            { "seo", JsonNode.Parse(Json)! },
            { "seo", JsonSerializer.SerializeToElement(JsonNode.Parse(Json)) },
            { "seo", Json },
            { "sections", JsonNode.Parse("""[{"type":"hero","internal":"x"}]""")! },
            { "sections", """[{"type":"hero","internal":"x"}]""" },
            { "sections", new List<ContentDocument> { new() { ["type"] = "hero", ["internal"] = "x" } } },
            { "sections", new[] { new Dictionary<string, object?> { ["type"] = "hero", ["internal"] = "x" } } },
            { "sections", JsonNode.Parse("""[{"type":"hero","details":{"internal":"x"}}]""")! },
            { "faq", JsonNode.Parse("""[{"internal":"x"}]""")! },
            { "faq", new List<ContentDocument> { new() { ["internal"] = "x" } } },
        };
    }

    [Theory]
    [MemberData(nameof(UnwritableShapes))]
    public async Task AHiddenFieldIsRefusedInEveryValueShape(string field, object value)
    {
        var entry = await _database.CreateAsync(Editor, Titled("Hello"));
        var before = _database.Footprint();

        var created = await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { [field] = value }));
        var updated = await Should.ThrowAsync<ValidationException>(() => _database.UpdateAsync(Editor, IdOf(entry), new() { [field] = value }, 1));
        var patched = await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(Editor, IdOf(entry), new() { [field] = value }, 1));

        created.Errors.ShouldAllBe(error => error.Code == AuthorizationErrorCodes.FieldNotWritable);
        updated.Errors.ShouldAllBe(error => error.Code == AuthorizationErrorCodes.FieldNotWritable);
        patched.Errors.ShouldAllBe(error => error.Code == AuthorizationErrorCodes.FieldNotWritable);
        _database.Footprint().ShouldBe(before);
    }

    [Fact]
    public async Task JsonThatRepeatsAHiddenKeyIsRefused()
    {
        const string Repeated = """{"metaTitle":"a","internal":"x","internal":"y"}""";

        await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["seo"] = Repeated }));
        await Should.ThrowAsync<ValidationException>(() => _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().CreateAsync("article", new() { ["seo"] = Repeated }, Token),
            origin: Origin.Seed));
        await Should.ThrowAsync<ValidationException>(() => _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().CreateAsync("article", new() { ["metadata"] = Repeated }, Token),
            origin: Origin.Seed));
    }

    [Fact]
    public async Task AFieldOutsideTheAllowListIsRefusedAndTheOthersAreWritten()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 5, ["region"] = "eu" });
        var role = _database.Database.AddRoleForTest("Limited", ("content.article.read", null), ("content.article.update", new PermissionConstraints { Fields = ["title"] }));
        var limited = WithRole(role);

        (await Should.ThrowAsync<ValidationException>(() => _database.UpdateAsync(limited, IdOf(entry), new() { ["title"] = "x", ["views"] = 6 }, 1)))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("views");
        (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(limited, IdOf(entry), new() { ["views"] = null }, 1)))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("views");

        // What it sends back unchanged is no write, and what it leaves out keeps its value.
        var read = (await _database.GetAsync(limited, IdOf(entry)))!;
        read["title"] = "Changed";
        read.Remove("region");
        var saved = await _database.UpdateAsync(limited, IdOf(entry), read, 1);

        saved["title"].ShouldBe("Changed");
        saved["views"].ShouldBe(5L);
        saved["region"].ShouldBe("eu");
    }

    [Fact]
    public async Task AFullUpdateIsNoWayToGuessAHiddenValue()
    {
        var entry = await _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().CreateAsync("article", new()
            {
                ["title"] = "Hello",
                ["secret"] = "right",
                ["seo"] = new ContentDocument { ["metaTitle"] = "Meta", ["internal"] = "right" },
                ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"Hi","internal":"right"}]"""),
            }, Token),
            origin: Origin.Seed);
        var id = IdOf(entry);
        var role = _database.Database.AddRoleForTest("Titles", ("content.article.read", null), ("content.article.update", new PermissionConstraints { Fields = ["title"] }));
        var limited = WithRole(role);

        foreach (var guess in new[] { "right", "wrong" })
        {
            (await Should.ThrowAsync<ValidationException>(() => _database.UpdateAsync(limited, id, new() { ["secret"] = guess }, 1)))
                .Errors.ShouldHaveSingleItem().Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable);
            (await Should.ThrowAsync<ValidationException>(() => _database.UpdateAsync(limited, id, new() { ["seo"] = new ContentDocument { ["metaTitle"] = "Meta", ["internal"] = guess } }, 1)))
                .Errors.ShouldHaveSingleItem().Path.ShouldBe("seo");
            (await Should.ThrowAsync<ValidationException>(() => _database.UpdateAsync(limited, id, new() { ["sections"] = JsonNode.Parse($$"""[{"type":"hero","heading":"Hi","internal":"{{guess}}"}]""") }, 1)))
                .Errors.ShouldHaveSingleItem().Path.ShouldBe("sections");
        }

        // What it read, with no hidden values in it, goes back as no change to them.
        var read = (await _database.GetAsync(limited, id))!;
        read["title"] = "Changed";
        var saved = await _database.UpdateAsync(limited, id, read, 1);

        saved["title"].ShouldBe("Changed");
        var shared = _database.Row("cms_article", id);
        shared["seo_internal"].ShouldBe("right");
        JsonNode.Parse((string)shared["sections"])![0]!["internal"]!.GetValue<string>().ShouldBe("right");
    }

    [Fact]
    public async Task AReadOnlyFieldIsTheEnginesToSet()
    {
        (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["stamp"] = "x" })))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable);

        var entry = await _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().CreateAsync("article", new() { ["stamp"] = "x" }, Token),
            origin: Origin.Job);

        entry["stamp"].ShouldBe("x");
    }

    [Fact]
    public async Task UniquenessHoldsAgainstADraftAndAPublishedEntry()
    {
        await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["code"] = "c1" });
        (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["code"] = "c2" })))
            .Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                error => error.Path.ShouldBe("slug"),
                error => error.Code.ShouldBe(ValidationErrorCodes.NotUnique));
        (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "Other", ["code"] = "c1" })))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("code");

        var published = await _database.CreateAsync(Editor, new() { ["title"] = "Live", ["code"] = "c3" });
        _database.Read(context =>
        {
            var localized = context.Rows("cms_article_i18n").AsNoTracking().Single(row => EF.Property<Guid>(row, "document_id") == IdOf(published));
            localized.Remove("id");
            localized["state"] = ContentState.Published;
            localized["slug"] = "published-only";
            context.Rows("cms_article_i18n").Add(localized);
            return context.SaveChanges();
        });

        (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "Published Only" })))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("slug");
    }

    [Fact]
    public async Task UniquenessIsPerSiteAndPerLocale()
    {
        var first = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["code"] = "c1" });

        await Should.NotThrowAsync(() => _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["code"] = "c1" }, site: SiteB));
        await Should.NotThrowAsync(() => _database.PatchAsync(Editor, IdOf(first), new() { ["title"] = "Hello" }, 0, locale: "fr"));
        await Should.NotThrowAsync(() => _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["code"] = "c2" }, locale: "fr-CA"));

        (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "Hello" }, locale: "fr")))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("slug");
    }

    [Fact]
    public async Task AnEntryIsNotItsOwnClashAndATrashedOneStillHoldsItsValue()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["code"] = "c1" });
        await Should.NotThrowAsync(() => _database.UpdateAsync(Editor, IdOf(entry), new() { ["title"] = "Hello", ["code"] = "c1" }, 1));

        await _database.DeleteAsync(Editor, IdOf(entry));

        (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "Other", ["code"] = "c1" })))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("code");
    }

    [Fact]
    public async Task AGlobalTypeIsUniqueAcrossSites()
    {
        var tag = await _database.CreateAsync(Editor, new() { ["name"] = "Net", ["code"] = "net" }, "tag");

        _database.Read(context => context.Documents.AsNoTracking().Single(document => document.DocumentId == IdOf(tag)).SiteId).ShouldBeNull();
        (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["name"] = "Other", ["code"] = "net" }, "tag", site: SiteB)))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("code");
        (await _database.GetAsync(Editor, IdOf(tag), type: "tag", site: SiteB)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ASingletonHasOneEntryPerSite()
    {
        await _database.CreateAsync(Editor, new() { ["name"] = "One" }, "settings");

        (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["name"] = "Two" }, "settings")))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(ContentErrorCodes.SingletonExists);
        await Should.NotThrowAsync(() => _database.CreateAsync(Editor, new() { ["name"] = "Two" }, "settings", site: SiteB));
    }

    [Fact]
    public async Task AReferenceWriteIsCorrectAcrossLocalizedAndSharedFields()
    {
        var ann = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var bob = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Bob" }, "author"));
        var tag1 = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "A" }, "tag"));
        var tag2 = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "B" }, "tag"));
        var media = Guid.CreateVersion7();

        var entry = await _database.CreateAsync(Editor, new()
        {
            ["title"] = "Hello",
            ["author"] = ann,
            ["sponsor"] = ann,
            ["tags"] = new[] { tag2, tag1 },
            ["seo"] = new ContentDocument { ["image"] = media },
        });
        var id = IdOf(entry);

        var french = await _database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour", ["sponsor"] = bob }, 0, locale: "fr");

        var rows = _database.References(id);
        rows.Select(row => (row.Field, row.Locale, row.Position, row.TargetType, row.TargetId, row.State)).ShouldBe(
        [
            ("author", "", 0, "author", ann, ContentState.Draft),
            ("seo.image", "", 0, "media", media, ContentState.Draft),
            ("sponsor", "en", 0, "author", ann, ContentState.Draft),
            ("sponsor", "fr", 0, "author", bob, ContentState.Draft),
            ("tags", "", 0, "tag", tag2, ContentState.Draft),
            ("tags", "", 1, "tag", tag1, ContentState.Draft),
        ], ignoreOrder: true);

        // Adding a locale left the shared values alone, and the engine reads them under the path it expects.
        ((ContentDocument)french["sponsor"]!)["id"].ShouldBe(bob);
        ((ContentDocument)french["author"]!)["id"].ShouldBe(ann);
        ((List<ContentDocument>)french["tags"]!).Select(tag => tag["id"]).ShouldBe([tag2, tag1]);
        ((ContentDocument)((ContentDocument)french["seo"]!)["image"]!)["id"].ShouldBe(media);

        // Changing one locale's relation leaves the other's.
        await _database.PatchAsync(Editor, id, new() { ["sponsor"] = null, ["tags"] = new[] { tag1 } }, 1);
        (await _database.GetAsync(Editor, id, locale: "fr"))!["sponsor"].ShouldNotBeNull();
        (await _database.GetAsync(Editor, id))!["sponsor"].ShouldBeNull();
        _database.References(id).Where(row => row.Field == "tags").ShouldHaveSingleItem().TargetId.ShouldBe(tag1);
    }

    [Fact]
    public async Task ReferencesInsideJsonAndRichTextAreRecordedUnderTheirField()
    {
        var ann = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var media = Guid.CreateVersion7();
        var linked = Guid.CreateVersion7();
        var sections = new JsonArray(new JsonObject { ["type"] = "hero", ["image"] = media.ToString(), ["cover"] = ann.ToString() });
        var body = $$$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"x","marks":[{"type":"entryLink","attrs":{"entryType":"author","entryId":"{{{linked}}}"}}]}]}]}""";

        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["sections"] = sections, ["body"] = body });

        var rows = _database.References(IdOf(entry));
        rows.Where(row => row.Field == "sections").Select(row => (row.TargetType, row.TargetId, row.Locale)).ShouldBe([("media", media, ""), ("author", ann, "")]);
        rows.Where(row => row.Field == "body").Select(row => (row.TargetType, row.TargetId, row.Locale)).ShouldBe([("author", linked, "en")]);

        var stored = JsonNode.Parse((string)_database.Row("cms_article", IdOf(entry))["sections"])![0]!;
        stored["image"]!["type"]!.GetValue<string>().ShouldBe("media");
        stored["cover"]!["id"]!.GetValue<string>().ShouldBe(ann.ToString());
    }

    [Fact]
    public async Task AddingALocaleNeedsTheExpectedVersionZeroAndKeepsTheOwner()
    {
        var owner = Guid.CreateVersion7();
        var author = AuthorRole(owner);
        var entry = await _database.CreateAsync(author, new() { ["title"] = "Hello", ["views"] = 3 });
        var id = IdOf(entry);

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => _database.UpdateAsync(author, id, Titled("Bonjour"), 1, locale: "fr"));
        conflict.CurrentVersion.ShouldBe(0);

        var french = await _database.UpdateAsync(author, id, Titled("Bonjour"), 0, locale: "fr");

        french["version"].ShouldBe(1);
        french["locale"].ShouldBe("fr");
        french["views"].ShouldBe(3L);
        _database.Read(context => context.Documents.AsNoTracking().Single(document => document.DocumentId == id && document.Locale == "fr").CreatedBy).ShouldBe(owner);
        _database.Versions(id).Select(version => (version.Locale, version.Number)).ShouldBe([("en", 1), ("fr", 1)]);

        await Should.ThrowAsync<EntryNotFoundException>(() => _database.UpdateAsync(AuthorRole(), id, Titled("Hallo"), 0, locale: "fr-CA"));
    }

    [Fact]
    public async Task AGrantLimitedToALocaleWritesOnlyThatLocale()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 5 });
        var role = _database.Database.AddRoleForTest("Translator", ("content.article.update", new PermissionConstraints { Locales = ["fr"] }), ("content.article.read", null));
        var translator = WithRole(role);

        var french = await _database.UpdateAsync(translator, IdOf(entry), Titled("Bonjour"), 0, locale: "fr");
        french["title"].ShouldBe("Bonjour");
        french["views"].ShouldBe(5L);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PatchAsync(translator, IdOf(entry), Titled("Changed"), 1));
        (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(translator, IdOf(entry), new() { ["views"] = 6 }, 1, locale: "fr")))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("views");
        (await _database.GetAsync(Editor, IdOf(entry)))!["views"].ShouldBe(5L);
    }

    [Fact]
    public async Task AChangeToASharedFieldMarksThePublishedOtherLocalesModified()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Hello"));
        var id = IdOf(entry);
        await _database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        await _database.PublishAsync(Editor, id);
        await _database.PublishAsync(Editor, id, "fr");

        await _database.PatchAsync(Editor, id, new() { ["subtitle"] = "Sub" }, 1);
        StatusOf(id, "fr").ShouldBe(EntryStatus.Published);
        StatusOf(id, "en").ShouldBe(EntryStatus.Modified);

        await _database.PatchAsync(Editor, id, new() { ["views"] = 4 }, 2);
        StatusOf(id, "fr").ShouldBe(EntryStatus.Modified);

        // Adding a locale that changes a shared field counts too.
        await _database.PublishAsync(Editor, id);
        StatusOf(id, "fr").ShouldBe(EntryStatus.Published);
        await _database.UpdateAsync(Editor, id, new() { ["title"] = "Salut", ["views"] = 9 }, 0, locale: "fr-CA");
        StatusOf(id, "en").ShouldBe(EntryStatus.Modified);
        StatusOf(id, "fr").ShouldBe(EntryStatus.Modified);
    }

    private EntryStatus StatusOf(Guid id, string locale) =>
        _database.Read(context => context.Documents.AsNoTracking().Single(document => document.DocumentId == id && document.Locale == locale).Status);

    [Fact]
    public async Task AWriteMayNotTakeAnEntryOutOfTheRuleThatAllowedIt()
    {
        using var database = new ServiceDatabase(cms => cms.Authorization.AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region")));
        var entry = await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["region"] = "eu" });
        var role = database.Database.AddRoleForTest("Regional", ("content.article.read", new PermissionConstraints { RowRule = "region" }), ("content.article.update", new PermissionConstraints { RowRule = "region" }));
        var regional = AuthorizationDatabase.User([role], [TokenScopes.Management], claims: new() { ["region"] = "eu" });

        await Should.NotThrowAsync(() => database.PatchAsync(regional, IdOf(entry), new() { ["subtitle"] = "Sub" }, 1));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => database.PatchAsync(regional, IdOf(entry), new() { ["region"] = "us" }, 2));
        (await database.GetAsync(Editor, IdOf(entry)))!["region"].ShouldBe("eu");
    }

    [FluentCMS.Schema.Collection("article")]
    private sealed class Regional : ContentItem
    {
        public string? Region { get; set; }
    }

    private sealed class Recorder : IEntryChangeSink
    {
        public List<EntryChange> Changes { get; } = [];

        public ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task TheChangeSinkHearsOnlyWhatCommitted()
    {
        var recorder = new Recorder();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IEntryChangeSink>(recorder));

        var entry = await database.CreateAsync(Editor, Titled("Hello"));
        await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(Editor, new() { ["views"] = 5000 }));
        await Should.ThrowAsync<VersionConflictException>(() => database.PatchAsync(Editor, IdOf(entry), Titled("x"), 7));
        await database.DeleteAsync(Editor, IdOf(entry));
        await database.RestoreAsync(Editor, IdOf(entry));

        recorder.Changes.Select(change => change.Kind).ShouldBe([EntryChangeKind.DraftSaved, EntryChangeKind.Trashed, EntryChangeKind.Restored]);
        recorder.Changes[0].Version.ShouldBe(1);

        // A write that was refused after it reached the tables is not told either.
        recorder.Changes.Clear();
        var role = database.Database.AddRoleForTest("Regional", ("content.article.create", new PermissionConstraints { RowRule = "nothing" }));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => database.CreateAsync(WithRole(role), Titled("Refused")));
        recorder.Changes.ShouldBeEmpty();
    }

    // Keep and skip (spec 4.2): a field its condition hides is not checked and keeps its value. A list holds a value it contains.
    [Fact]
    public async Task AFieldItsConditionHidesIsNotCheckedAndKeepsItsValue()
    {
        var model = SampleSchema.NewBuilder()
            .AddCollection("clip", type => type
                .Field("labels", "select", field => field.Multiple().Setting(FieldSettings.Options, Clip))
                .Field("url", "text", field => field.Length(3, 5).VisibleWhen(Condition.Field("labels").Equals("video"))))
            .Build();
        using var database = new ServiceDatabase(schema: model);

        var hidden = IdOf(await database.CreateAsync(Editor, new() { ["labels"] = News, ["url"] = "ab" }, "clip"));
        await database.PatchAsync(Editor, hidden, new() { ["labels"] = Array.Empty<string>() }, 1, "clip");
        (await database.GetAsync(Editor, hidden, type: "clip"))!["url"].ShouldBe("ab");

        var shown = await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(Editor, new() { ["labels"] = Clip, ["url"] = "ab" }, "clip"));
        shown.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Path.ShouldBe("url"),
            error => error.Code.ShouldBe(ValidationErrorCodes.MinLength));
    }

    // A hidden value is kept, so it is still held to the length of the column that keeps it, on every provider.
    [Fact]
    public async Task AFieldItsConditionHidesMustStillFitItsColumn()
    {
        var model = SampleSchema.NewBuilder()
            .AddComponent("clip-seo", component => component
                .Field("caption", "text", field => field.Length(null, 5))
                .Field("note", "text", field => field.Length(null, 5).VisibleWhen(Condition.Field("caption").IsEmpty())))
            .AddCollection("clip", type => type
                .Field("labels", "select", field => field.Multiple().Setting(FieldSettings.Options, Clip))
                .Field("url", "text", field => field.Length(null, 5).VisibleWhen(Condition.Field("labels").Equals("video")))
                .Field("seo", "component", field => field.Setting(FieldSettings.Component, "clip-seo").VisibleWhen(Condition.Field("labels").Equals("video"))))
            .Build();
        using var database = new ServiceDatabase(schema: model);

        var refused = await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(
            Editor,
            new() { ["labels"] = News, ["url"] = "far too long", ["seo"] = new ContentDocument { ["caption"] = "far too long" } },
            "clip"));
        refused.Errors.Select(error => (error.Path, error.Code)).ShouldBe(
            [("url", ValidationErrorCodes.MaxLength), ("seo.caption", ValidationErrorCodes.MaxLength)],
            ignoreOrder: true);

        var inner = await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(
            Editor,
            new() { ["labels"] = Clip, ["url"] = "short", ["seo"] = new ContentDocument { ["caption"] = "hi", ["note"] = "far too long" } },
            "clip"));
        inner.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Path.ShouldBe("seo.note"),
            error => error.Code.ShouldBe(ValidationErrorCodes.MaxLength));
    }
}
