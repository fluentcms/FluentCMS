using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class PublishingTransitionTests : IDisposable
{
    private static readonly string[] Labels = ["net"];
    private static readonly string[] Notes = ["note"];

    private readonly Recorder _sink = new();
    private readonly ServiceDatabase _database;

    public PublishingTransitionTests()
    {
        _database = new ServiceDatabase(cms => cms.Services.AddSingleton<IEntryChangeSink>(_sink));
    }

    public void Dispose() => _database.Dispose();

    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private sealed class Recorder : IEntryChangeSink
    {
        public List<EntryChange> Changes { get; } = [];

        public ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    private async Task<Guid> PublishedAsync(string title = "Hello")
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled(title)));
        await _database.PublishAsync(Editor, id);
        return id;
    }

    [Fact]
    public async Task PublishCopiesTheDraftRowsAndRecordsTheVersion()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        await _database.PublishAsync(Editor, author, type: "author");
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 5, ["author"] = author }));
        (await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published })).ShouldBeNull();

        var result = await _database.PublishAsync(Editor, id);

        result.Warnings.ShouldBeEmpty();
        result.Entry["status"].ShouldBe(EntryStatus.Published);
        result.Entry["version"].ShouldBe(1);

        var document = _database.Document(id);
        document.Status.ShouldBe(EntryStatus.Published);
        document.DraftVersion.ShouldBe(1);
        document.PublishedVersion.ShouldBe(1);
        document.PublishedAt.ShouldNotBeNull();
        document.FirstPublishedAt.ShouldBe(document.PublishedAt);

        _database.PublishedRow("cms_article", id)!["views"].ShouldBe(5L);
        _database.PublishedRow("cms_article_i18n", id, "en")!["title"].ShouldBe("Hello");
        _database.Row("cms_article", id).ShouldNotBeNull();
        _database.References(id, ContentState.Published).ShouldHaveSingleItem().TargetId.ShouldBe(author);
        _database.Versions(id).ShouldHaveSingleItem().StatusAfter.ShouldBe(EntryStatus.Published);

        var read = await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published });
        read!["title"].ShouldBe("Hello");
        read["status"].ShouldBe(EntryStatus.Published);

        var change = _sink.Changes[^1];
        change.Kind.ShouldBe(EntryChangeKind.Published);
        change.Id.ShouldBe(id);
        change.Locale.ShouldBe("en");
        change.Version.ShouldBe(1);
    }

    [Fact]
    public async Task ASaveAfterPublishingLeavesThePublishedRowsAndTheNextPublishMovesThem()
    {
        var id = await PublishedAsync();
        var first = _database.Document(id).PublishedAt;

        await _database.PatchAsync(Editor, id, Titled("Changed"), 1);
        _database.Document(id).Status.ShouldBe(EntryStatus.Modified);
        _database.Document(id).PublishedVersion.ShouldBe(1);
        _database.PublishedRow("cms_article_i18n", id, "en")!["title"].ShouldBe("Hello");
        (await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published }))!["title"].ShouldBe("Hello");

        await _database.PublishAsync(Editor, id);
        var document = _database.Document(id);
        document.Status.ShouldBe(EntryStatus.Published);
        document.PublishedVersion.ShouldBe(2);
        document.FirstPublishedAt.ShouldBe(first);
        _database.PublishedRow("cms_article_i18n", id, "en")!["title"].ShouldBe("Changed");
    }

    [Fact]
    public async Task UnpublishRemovesThePublishedRowsAndKeepsTheDraft()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["author"] = author }));
        await _database.PublishAsync(Editor, id);
        var first = _database.Document(id).FirstPublishedAt;
        _sink.Changes.Clear();

        var entry = await _database.UnpublishAsync(Editor, id);

        entry["status"].ShouldBe(EntryStatus.Unpublished);
        var document = _database.Document(id);
        document.Status.ShouldBe(EntryStatus.Unpublished);
        document.PublishedVersion.ShouldBeNull();
        document.PublishedAt.ShouldBeNull();
        document.FirstPublishedAt.ShouldBe(first);
        document.DraftVersion.ShouldBe(1);
        _database.PublishedRow("cms_article", id).ShouldBeNull();
        _database.PublishedRow("cms_article_i18n", id, "en").ShouldBeNull();
        _database.References(id, ContentState.Published).ShouldBeEmpty();
        _database.References(id, ContentState.Draft).ShouldHaveSingleItem();
        (await _database.GetAsync(Editor, id))!["title"].ShouldBe("Hello");
        (await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published })).ShouldBeNull();

        var change = _sink.Changes.ShouldHaveSingleItem();
        change.Kind.ShouldBe(EntryChangeKind.Unpublished);
        change.Version.ShouldBe(1);

        await _database.PublishAsync(Editor, id);
        _database.Document(id).Status.ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task ADraftThatWasNeverPublishedCannotBeUnpublishedOrDiscarded()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("Hello")));

        (await Should.ThrowAsync<ValidationException>(() => _database.UnpublishAsync(Editor, id))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ContentErrorCodes.NotPublished);
        (await Should.ThrowAsync<ValidationException>(() => _database.DiscardAsync(Editor, id))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ContentErrorCodes.NotPublished);
    }

    [Fact]
    public async Task DiscardPutsThePublishedRowsBackAsANewVersion()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var other = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Bob" }, "author"));
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1, ["author"] = author }));
        await _database.PublishAsync(Editor, id);
        await _database.PatchAsync(Editor, id, new() { ["title"] = "Changed", ["views"] = 2, ["author"] = other }, 1);
        _sink.Changes.Clear();

        var entry = await _database.DiscardAsync(Editor, id);

        entry["title"].ShouldBe("Hello");
        entry["version"].ShouldBe(3);
        entry["status"].ShouldBe(EntryStatus.Published);
        var document = _database.Document(id);
        document.Status.ShouldBe(EntryStatus.Published);
        document.DraftVersion.ShouldBe(3);
        document.PublishedVersion.ShouldBe(1);
        _database.Row("cms_article", id)["views"].ShouldBe(1L);
        _database.References(id, ContentState.Draft).ShouldHaveSingleItem().TargetId.ShouldBe(author);

        var version = _database.Versions(id)[^1];
        version.Number.ShouldBe(3);
        version.Message.ShouldNotBeNullOrEmpty();
        var change = _sink.Changes.ShouldHaveSingleItem();
        change.Kind.ShouldBe(EntryChangeKind.DraftDiscarded);
        change.Version.ShouldBe(3);
    }

    [Fact]
    public async Task DiscardingADraftThatEqualsThePublishedStateWritesNothing()
    {
        var id = await PublishedAsync();
        var before = _database.Trace(id);
        _sink.Changes.Clear();

        (await _database.DiscardAsync(Editor, id))["version"].ShouldBe(1);

        _database.Trace(id).ShouldBe(before);
        _sink.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASaveThatEditsTheDraftBackLeavesItPublishedAndDiscardAgrees()
    {
        var id = await PublishedAsync("One");
        (await _database.PatchAsync(Editor, id, Titled("Two"), 1))["status"].ShouldBe(EntryStatus.Modified);

        (await _database.PatchAsync(Editor, id, Titled("One"), 2))["status"].ShouldBe(EntryStatus.Published);
        _database.Document(id).Status.ShouldBe(EntryStatus.Published);

        var entry = await _database.DiscardAsync(Editor, id);

        entry["status"].ShouldBe(EntryStatus.Published);
        entry["version"].ShouldBe(3);
        _database.Versions(id).Count.ShouldBe(3);
    }

    // A row written before saves decided the status by content can be Modified with nothing to discard.
    [Fact]
    public async Task DiscardingADraftLeftModifiedThatEqualsThePublishedStatePutsTheStatusRightAndWritesNoVersion()
    {
        var id = await PublishedAsync();
        _database.Read(context => context.Documents.Where(document => document.DocumentId == id).ExecuteUpdate(set => set.SetProperty(document => document.Status, EntryStatus.Modified)));

        var entry = await _database.DiscardAsync(Editor, id);

        entry["status"].ShouldBe(EntryStatus.Published);
        entry["version"].ShouldBe(1);
        _database.Document(id).Status.ShouldBe(EntryStatus.Published);
        _database.Versions(id).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ASharedEditThatIsTakenBackGetsTheSameStatusFromSaveRecomputeAndDiscard()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await _database.PatchAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        await _database.PublishAsync(Editor, id);
        await _database.PublishAsync(Editor, id, "fr");

        await _database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 1);
        _database.Document(id).Status.ShouldBe(EntryStatus.Modified);
        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Modified);

        // The save decides the status of fr, the recompute that of en.
        (await _database.PatchAsync(Editor, id, new() { ["views"] = 1 }, 1, locale: "fr"))["status"].ShouldBe(EntryStatus.Published);
        _database.Document(id).Status.ShouldBe(EntryStatus.Published);
        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Published);

        var english = await _database.DiscardAsync(Editor, id);
        var french = await _database.DiscardAsync(Editor, id, "fr");

        english["status"].ShouldBe(EntryStatus.Published);
        english["version"].ShouldBe(2);
        french["status"].ShouldBe(EntryStatus.Published);
        french["version"].ShouldBe(2);
        _database.PublishedRow("cms_article", id)!["views"].ShouldBe(1L);
    }

    [Fact]
    public async Task OnlyPublishingMarksAVersionPublishedSoDiscardsCannotPassTheCap()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("note", type => type.PublishPolicy(UnpublishedReferencePolicy.Warn, 2, PublishPolicyAttribute.DefaultTrashRetentionDays).Field("title", "text")).Build();
        using var database = new ServiceDatabase(schema: schema);
        var id = IdOf(await database.CreateAsync(Editor, Titled("published"), "note"));
        await database.PublishAsync(Editor, id, type: "note");

        var version = 1;
        for (var round = 0; round < 10; round++)
        {
            version = VersionOf(await database.PatchAsync(Editor, id, Titled($"edit {round}"), version, "note"));
            var discarded = await database.DiscardAsync(Editor, id, type: "note");
            discarded["status"].ShouldBe(EntryStatus.Published);
            version = VersionOf(discarded);
        }

        var versions = await database.VersionsAsync(Editor, id, type: "note");
        versions.Count.ShouldBe(2);
        versions.Count(item => item.Status == EntryStatus.Published).ShouldBe(1);
        versions.Single(item => item.Status == EntryStatus.Published).Number.ShouldBe(1);
    }

    [Fact]
    public async Task RestoringAVersionMakesANewDraftAndLeavesThePublishedRows()
    {
        var id = await PublishedAsync("One");
        await _database.PatchAsync(Editor, id, Titled("Two"), 1);
        await _database.PatchAsync(Editor, id, Titled("Three"), 2);
        await _database.PublishAsync(Editor, id);
        _sink.Changes.Clear();

        var entry = await _database.RestoreVersionAsync(Editor, id, 1);

        entry["title"].ShouldBe("One");
        entry["version"].ShouldBe(4);
        entry["status"].ShouldBe(EntryStatus.Modified);
        _database.PublishedRow("cms_article_i18n", id, "en")!["title"].ShouldBe("Three");
        _database.Versions(id)[^1].Message.ShouldBe("Restored version 1.");

        var change = _sink.Changes.ShouldHaveSingleItem();
        change.Kind.ShouldBe(EntryChangeKind.VersionRestored);
        change.Version.ShouldBe(4);

        await _database.RestoreVersionAsync(Editor, id, 3);
        _database.Document(id).Status.ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task RestoringAVersionBringsBackEveryKindOfValue()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var tag = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "net" }, "tag"));
        var body = new RichText("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Body"}]}]}""");
        var at = new DateTimeOffset(2026, 11, 2, 10, 30, 0, TimeSpan.Zero);
        var id = IdOf(await _database.CreateAsync(Editor, new()
        {
            ["title"] = "One",
            ["body"] = body,
            ["views"] = 7,
            ["price"] = 1.5m,
            ["publishedOn"] = at,
            ["issued"] = new DateOnly(2026, 11, 2),
            ["author"] = author,
            ["tags"] = new[] { tag },
            ["labels"] = Labels,
            ["seo"] = new ContentDocument { ["metaTitle"] = "Meta" },
            ["sections"] = new object[] { new ContentDocument { ["type"] = "hero", ["heading"] = "Hi", ["on"] = "2026-11-02" } },
            ["metadata"] = new System.Text.Json.Nodes.JsonObject { ["a"] = 1 },
        }));
        var original = _database.Versions(id)[0].Snapshot;

        await _database.UpdateAsync(Editor, id, Titled("Two"), 1);
        var restored = await _database.RestoreVersionAsync(Editor, id, 1);

        restored["version"].ShouldBe(3);
        _database.Versions(id)[^1].Snapshot.ShouldBe(original);
        _database.References(id, ContentState.Draft).Count.ShouldBe(2);
    }

    [Fact]
    public async Task AVersionOrTheEntryThatIsNotThereCannotBeRestored()
    {
        var id = await PublishedAsync();

        await Should.ThrowAsync<EntryNotFoundException>(() => _database.RestoreVersionAsync(Editor, id, 9));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.RestoreVersionAsync(Editor, Guid.CreateVersion7(), 1));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.DiffAsync(Editor, id, 1, 9));
        (await _database.VersionAsync(Editor, id, 9)).ShouldBeNull();
    }

    [Fact]
    public async Task ATrashedEntryTakesNoTransition()
    {
        var id = await PublishedAsync();
        await _database.DeleteAsync(Editor, id);

        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PublishAsync(Editor, id));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.UnpublishAsync(Editor, id));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.DiscardAsync(Editor, id));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.RestoreVersionAsync(Editor, id, 1));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.VersionsAsync(Editor, id));

        await _database.RestoreAsync(Editor, id);
        _database.Document(id).Status.ShouldBe(EntryStatus.Published);
        _database.PublishedRow("cms_article_i18n", id, "en").ShouldNotBeNull();
    }

    [Fact]
    public async Task AStaleExpectedVersionStopsAPublishADiscardAndARestore()
    {
        var id = await PublishedAsync();
        await _database.PatchAsync(Editor, id, Titled("Changed"), 1);
        var before = _database.Trace(id);

        (await Should.ThrowAsync<VersionConflictException>(() => _database.PublishAsync(Editor, id, expected: 1))).CurrentVersion.ShouldBe(2);
        await Should.ThrowAsync<VersionConflictException>(() => _database.DiscardAsync(Editor, id, expected: 1));
        await Should.ThrowAsync<VersionConflictException>(() => _database.RestoreVersionAsync(Editor, id, 1, expected: 1));

        _database.Trace(id).ShouldBe(before);
        await Should.NotThrowAsync(() => _database.PublishAsync(Editor, id, expected: 2));
    }

    [Fact]
    public async Task PublishingOneLocaleLeavesTheOtherAlone()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("Hello")));
        await _database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");

        await _database.PublishAsync(Editor, id, "fr");

        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Published);
        _database.Document(id).Status.ShouldBe(EntryStatus.Draft);
        _database.PublishedRow("cms_article_i18n", id, "fr").ShouldNotBeNull();
        _database.PublishedRow("cms_article_i18n", id, "en").ShouldBeNull();
        _sink.Changes[^1].Locale.ShouldBe("fr");
    }

    [Fact]
    public async Task TheLastLocaleTakesTheSharedRowWithItWhenItIsUnpublished()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["author"] = author }));
        await _database.PatchAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        await _database.PublishAsync(Editor, id);
        await _database.PublishAsync(Editor, id, "fr");

        await _database.UnpublishAsync(Editor, id);
        _database.PublishedRow("cms_article", id).ShouldNotBeNull();
        _database.References(id, ContentState.Published).ShouldHaveSingleItem();
        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Published);
        (await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published }, locale: "fr"))!["title"].ShouldBe("Bonjour");

        await _database.UnpublishAsync(Editor, id, "fr");
        _database.PublishedRow("cms_article", id).ShouldBeNull();
        _database.References(id, ContentState.Published).ShouldBeEmpty();
    }

    [Fact]
    public async Task PublishingASharedChangeResettlesTheOtherLocales()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await _database.PatchAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        await _database.PublishAsync(Editor, id);
        await _database.PublishAsync(Editor, id, "fr");

        await _database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 1);
        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Modified);

        await _database.PublishAsync(Editor, id);

        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Published);
        (await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published }, locale: "fr"))!["views"].ShouldBe(2L);
    }

    [Fact]
    public async Task DiscardingASharedChangeResettlesTheOtherLocales()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await _database.PatchAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        await _database.PublishAsync(Editor, id);
        await _database.PublishAsync(Editor, id, "fr");
        await _database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 1);
        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Modified);

        await _database.DiscardAsync(Editor, id);

        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Published);
        _database.Row("cms_article", id)["views"].ShouldBe(1L);
    }

    [Fact]
    public async Task ASingletonAndAGlobalTypePublishToo()
    {
        var settings = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Site" }, "settings"));
        (await _database.PublishAsync(Editor, settings, type: "settings")).Entry["status"].ShouldBe(EntryStatus.Published);
        _database.PublishedRow("cms_settings", settings).ShouldNotBeNull();

        var tag = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "net" }, "tag"));
        (await _database.PublishAsync(Editor, tag, type: "tag")).Entry["status"].ShouldBe(EntryStatus.Published);
        _database.PublishedRow("cms_tag", tag).ShouldNotBeNull();
        (await _database.UnpublishAsync(Editor, tag, type: "tag"))["status"].ShouldBe(EntryStatus.Unpublished);
    }

    [Fact]
    public async Task PublishRunsEveryRuleADraftSkips()
    {
        var schema = SampleSchema.NewBuilder()
            .AddBlock("note", block => block.Field("text", "richText", field => field.Required()))
            .AddCollection("page", type => type
                .Field("name", "text", field => field.Required())
                .Field("body", "richText", field => field.Required())
                .Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, Notes))
                .Field("flag", "boolean")
                .Field("reason", "text", field => field.RequiredWhen(Condition.Field("flag").Equals(true))))
            .Build();
        using var database = new ServiceDatabase(schema: schema);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["flag"] = true }, "page"));

        var errors = (await Should.ThrowAsync<ValidationException>(() => database.PublishAsync(Editor, id, type: "page"))).Errors;
        errors.Select(error => (error.Path, error.Code)).ShouldBe(
            [("name", ValidationErrorCodes.Required), ("body", ValidationErrorCodes.Required), ("reason", ValidationErrorCodes.RequiredByCondition)],
            ignoreOrder: true);

        database.Document(id, "").Status.ShouldBe(EntryStatus.Draft);
        database.PublishedRow("cms_page", id).ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph"}]}""")]
    [InlineData("""{"type":"doc","content":[]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph"},{"type":"paragraph"}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"   "}]}]}""")]
    public async Task AnEmptyDocumentIsMissingOnPublish(string json)
    {
        var schema = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("body", "richText", field => field.Required())).Build();
        using var database = new ServiceDatabase(schema: schema);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["body"] = new RichText(json) }, "page"));

        (await Should.ThrowAsync<ValidationException>(() => database.PublishAsync(Editor, id, type: "page"))).Errors.ShouldHaveSingleItem().Path.ShouldBe("body");
    }

    [Fact]
    public async Task AnEmptyDocumentInsideABlockIsMissingAndADocumentWithAnImageIsNot()
    {
        var schema = SampleSchema.NewBuilder()
            .AddBlock("note", block => block.Field("text", "richText", field => field.Required()))
            .AddCollection("page", type => type.Field("sections", "blocks", field => field.Setting(FieldSettings.Blocks, Notes)))
            .Build();
        using var database = new ServiceDatabase(schema: schema);
        var empty = new ContentDocument { ["type"] = "note", ["text"] = RichText.Empty };
        var id = IdOf(await database.CreateAsync(Editor, new() { ["sections"] = new object[] { empty } }, "page"));

        (await Should.ThrowAsync<ValidationException>(() => database.PublishAsync(Editor, id, type: "page"))).Errors.ShouldHaveSingleItem().Path.ShouldBe("sections[0].text");

        var rule = new ContentDocument { ["type"] = "note", ["text"] = new RichText("""{"type":"doc","content":[{"type":"horizontalRule"}]}""") };
        await database.PatchAsync(Editor, id, new() { ["sections"] = new object[] { rule } }, 1, "page");
        (await database.PublishAsync(Editor, id, type: "page")).Entry["status"].ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task ATitleOfWhitespaceAndAnEmptyStringAreMissingOnPublish()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["views"] = 1 }));
        _database.Row("cms_article_i18n", id, "en")["title"].ShouldBe("");
        (await Should.ThrowAsync<ValidationException>(() => _database.PublishAsync(Editor, id))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCodes.Required);

        await _database.PatchAsync(Editor, id, Titled("   "), 1);
        (await Should.ThrowAsync<ValidationException>(() => _database.PublishAsync(Editor, id))).Errors.ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCodes.Required);
    }

    [Fact]
    public async Task TwoEntriesWithAnEmptySlugClashOnPublishAsAnErrorAndNotAtTheIndex()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("slug", "slug", field => field.Unique())).Build();
        using var database = new ServiceDatabase(schema: schema);
        var first = IdOf(await database.CreateAsync(Editor, [], "page"));
        var second = IdOf(await database.CreateAsync(Editor, [], "page"));
        await database.PublishAsync(Editor, first, type: "page");

        var error = (await Should.ThrowAsync<ValidationException>(() => database.PublishAsync(Editor, second, type: "page"))).Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(ValidationErrorCodes.NotUnique);
    }

    private sealed class Stamp : IBeforePublishHook<ContentDocument>
    {
        public ValueTask BeforePublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry["stamp"] = "from-hook";
            context.Entry["secret"] = "from-hook";
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task WhatABeforePublishHookChangesIsANewVersionAndIsWhatIsPublished()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforePublishHook<ContentDocument>, Stamp>());
        var id = IdOf(await database.CreateAsync(Editor, Titled("Hello")));

        var result = await database.PublishAsync(Editor, id);

        result.Entry["version"].ShouldBe(2);
        result.Entry["stamp"].ShouldBe("from-hook");
        result.Entry.ContainsKey("secret").ShouldBeFalse();
        var document = database.Document(id);
        document.DraftVersion.ShouldBe(2);
        document.PublishedVersion.ShouldBe(2);
        document.Status.ShouldBe(EntryStatus.Published);
        database.PublishedRow("cms_article", id)!["secret"].ShouldBe("from-hook");
        database.Row("cms_article", id)["secret"].ShouldBe("from-hook");
        database.Versions(id).Select(version => version.StatusAfter).ShouldBe([EntryStatus.Draft, EntryStatus.Published]);
    }

    [Fact]
    public async Task PruningDropsTheOldestDraftsAndKeepsEveryVersionThatWasPublished()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("page", type => type.PublishPolicy(UnpublishedReferencePolicy.Warn, 3, PublishPolicyAttribute.DefaultTrashRetentionDays).Field("name", "text")).Build();
        using var database = new ServiceDatabase(schema: schema);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "v1" }, "page"));
        await database.PublishAsync(Editor, id, type: "page");

        for (var number = 2; number <= 6; number++)
        {
            await database.PatchAsync(Editor, id, new() { ["name"] = $"v{number}" }, number - 1, "page");
            if (number == 3)
            {
                await database.PublishAsync(Editor, id, type: "page");
            }
        }

        database.Versions(id).Select(version => version.Number).ShouldBe([1, 3, 6]);

        await database.PublishAsync(Editor, id, type: "page");
        for (var number = 7; number <= 9; number++)
        {
            await database.PatchAsync(Editor, id, new() { ["name"] = $"v{number}" }, number - 1, "page");
        }

        database.Versions(id).Select(version => version.Number).ShouldBe([1, 3, 6, 9]);
        (await database.RestoreVersionAsync(Editor, id, 1, type: "page"))["name"].ShouldBe("v1");
        await Should.ThrowAsync<EntryNotFoundException>(() => database.RestoreVersionAsync(Editor, id, 2, type: "page"));
    }

    [Fact]
    public async Task TheDefaultCapIsFiftyPerLocale()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("v1")));
        await _database.PatchAsync(Editor, id, Titled("fr"), 0, locale: "fr");
        for (var number = 2; number <= 52; number++)
        {
            await _database.PatchAsync(Editor, id, Titled($"v{number}"), number - 1);
        }

        var versions = _database.Versions(id);
        versions.Count(version => version.Locale == "en").ShouldBe(50);
        versions.Count(version => version.Locale == "fr").ShouldBe(1);
        versions.Where(version => version.Locale == "en").Min(version => version.Number).ShouldBe(3);
    }

    [Fact]
    public async Task VersionsListNewestFirstWithWhoMadeThem()
    {
        var id = await PublishedAsync();
        await _database.PatchAsync(Editor, id, Titled("Changed"), 1);

        var versions = await _database.VersionsAsync(Editor, id);

        versions.Select(version => version.Number).ShouldBe([2, 1]);
        versions[0].Status.ShouldBe(EntryStatus.Modified);
        versions[1].Status.ShouldBe(EntryStatus.Published);
        versions[0].AuthorId.ShouldBe(Editor.Id);
        versions[0].Origin.ShouldBe(Origin.Api);
        versions[0].ClientId.ShouldBe("client");
        versions[0].Locale.ShouldBe("en");
    }

    [Fact]
    public async Task AVersionReadsLikeTheEntryItWas()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["views"] = 3, ["author"] = author, ["seo"] = new ContentDocument { ["metaTitle"] = "Meta" } }));
        await _database.PatchAsync(Editor, id, Titled("Two"), 1);

        var version = (await _database.VersionAsync(Editor, id, 1))!;

        version["id"].ShouldBe(id);
        version["version"].ShouldBe(1);
        version["locale"].ShouldBe("en");
        version["title"].ShouldBe("One");
        version["views"].ShouldBe(3L);
        ((ContentDocument)version["author"]!)["id"].ShouldBe(author);
        ((ContentDocument)version["seo"]!)["metaTitle"].ShouldBe("Meta");
    }

    [Fact]
    public async Task TheTypedServiceGoesThroughTheSameTransitions()
    {
        using var database = new ServiceDatabase(schema: SampleSchema.FromClasses().Build());
        await database.AsAsync(Editor, async services =>
        {
            var service = services.GetRequiredService<IContentService<FluentCMS.Core.Tests.Schema.Samples.Author>>();
            var created = await service.CreateAsync(new FluentCMS.Core.Tests.Schema.Samples.Author { Name = "Ann" }, Token);

            var published = await service.PublishAsync(created.Id, cancellationToken: Token);
            published.Entry.Status.ShouldBe(EntryStatus.Published);
            published.Warnings.ShouldBeEmpty();

            var changed = await service.PatchAsync(created.Id, new() { ["name"] = "Bob" }, 1, Token);
            changed.Status.ShouldBe(EntryStatus.Modified);
            (await service.DiffAsync(created.Id, 1, 2, cancellationToken: Token)).ShouldHaveSingleItem().Path.ShouldBe("name");
            (await service.GetVersionsAsync(created.Id, cancellationToken: Token)).Count.ShouldBe(2);
            (await service.GetVersionAsync(created.Id, 1, cancellationToken: Token))!.Name.ShouldBe("Ann");

            (await service.DiscardDraftAsync(created.Id, cancellationToken: Token)).Name.ShouldBe("Ann");
            (await service.RestoreVersionAsync(created.Id, 2, cancellationToken: Token)).Name.ShouldBe("Bob");
            (await service.UnpublishAsync(created.Id, cancellationToken: Token)).Status.ShouldBe(EntryStatus.Unpublished);
            return true;
        });
    }

    [Fact]
    public void ThePolicyIsReadFromAnAttributeAndRoundTripsThroughJson()
    {
        var model = SampleSchema.NewBuilder().AddContentType<Blocked>().Build();
        var type = model.FindContentType("blocked")!;
        type.UnpublishedReferences.ShouldBe(UnpublishedReferencePolicy.Block);
        type.MaxVersions.ShouldBe(5);

        var json = FluentCMS.Core.Schema.SchemaJsonWriter.Write(model);
        json.ShouldContain("\"unpublishedReferences\": \"block\"");
        json.ShouldContain("\"maxVersions\": 5");

        SampleSchema.NewBuilder().AddCollection("plain", plain => plain.Field("name", "text")).Build().FindContentType("plain")!.MaxVersions.ShouldBe(50);
        FluentCMS.Core.Schema.SchemaJsonWriter.Write(SampleSchema.NewBuilder().AddCollection("plain", plain => plain.Field("name", "text")).Build()).ShouldNotContain("maxVersions");
    }

    [FluentCMS.Schema.Collection("blocked")]
    [PublishPolicy(UnpublishedReferences = UnpublishedReferencePolicy.Block, MaxVersions = 5)]
    private sealed class Blocked : ContentItem
    {
        [Text]
        public string? Name { get; set; }
    }
}
