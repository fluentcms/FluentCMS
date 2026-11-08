using System.Data.Common;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Core.Tests.Schema.Samples;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Data.Tests.Query;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class ContentServiceRulesAcrossFieldsTests
{
    private sealed class VideoNeedsUrl : IEntryValidator<Article>
    {
        public ValueTask ValidateAsync(Article entry, ValidationContext context, CancellationToken cancellationToken)
        {
            if (entry.Category == ArticleCategory.Video && string.IsNullOrEmpty(entry.VideoUrl))
            {
                context.AddError("videoUrl", "X-RULE", "A video needs its url.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private static ServiceDatabase Database() =>
        new(cms => cms.Services.AddSingleton<IEntryValidator<Article>, VideoNeedsUrl>(), SampleSchema.FromClasses().Build());

    private static Guid Limited(ServiceDatabase database) => database.Database.AddRoleForTest(
        "Limited",
        ("content.article.read", new PermissionConstraints { Fields = ["title", "category"] }),
        ("content.article.update", new PermissionConstraints { Fields = ["title", "category"] }),
        ("content.article.create", new PermissionConstraints { Fields = ["title", "category"] }));

    [Fact]
    public async Task ARuleAcrossFieldsCannotBeDodgedByNotBeingAbleToReadTheField()
    {
        using var database = Database();
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "T", ["category"] = "news" }));
        var before = database.Footprint();

        (await Should.ThrowAsync<ValidationException>(() => database.PatchAsync(Editor, id, new() { ["category"] = "video" }, 1)))
            .Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                error => error.Path.ShouldBe("videoUrl"),
                error => error.Code.ShouldBe("X-RULE"));

        var refused = await Should.ThrowAsync<ValidationException>(() => database.PatchAsync(WithRole(Limited(database)), id, new() { ["category"] = "video" }, 1));

        var produced = refused.Errors.ShouldHaveSingleItem();
        produced.Path.ShouldBe("");
        produced.Code.ShouldBe(ValidationErrorCodes.InvalidValue);
        produced.Message.ShouldNotContain("video", Case.Insensitive);
        database.Footprint().ShouldBe(before);
        (await database.GetAsync(Editor, id))!["category"].ShouldBe("news");
    }

    [Fact]
    public async Task ACreateThatBreaksARuleOnAnUnreadableFieldFailsWithoutNamingIt()
    {
        using var database = Database();

        var refused = await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(WithRole(Limited(database)), new() { ["title"] = "T", ["category"] = "video" }));

        refused.Errors.ShouldHaveSingleItem().Path.ShouldBe("");
    }

    [Fact]
    public async Task ARuleAlreadyBrokenInTheStoredEntryDoesNotBlockAnUnrelatedWrite()
    {
        using var database = Database();
        var id = IdOf(await database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().CreateAsync("article", new() { ["title"] = "T", ["category"] = "news" }, Token)));
        database.Read(context => context.Rows("cms_article").Where(row => EF.Property<Guid>(row, "document_id") == id).ExecuteUpdate(set => set.SetProperty(row => EF.Property<string?>(row, "category"), "video")));

        (await database.PatchAsync(WithRole(Limited(database)), id, new() { ["title"] = "Changed" }, 1))["title"].ShouldBe("Changed");
    }

    [Fact]
    public async Task AValueAClientSentIsReportedEvenWhenItEqualsTheStoredOne()
    {
        using var database = new ServiceDatabase();
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "T", ["views"] = 5 }));
        database.Read(context => context.Rows("cms_article").Where(row => EF.Property<Guid>(row, "document_id") == id).ExecuteUpdate(set => set.SetProperty(row => EF.Property<long?>(row, "views"), 5000L)));
        var role = database.Database.AddRoleForTest("WriteOnly", ("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.article.update", null));

        foreach (var guess in new[] { 6000, 5000 })
        {
            (await Should.ThrowAsync<ValidationException>(() => database.PatchAsync(WithRole(role), id, new() { ["views"] = guess }, 1)))
                .Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                    error => error.Path.ShouldBe("views"),
                    error => error.Code.ShouldBe(ValidationErrorCodes.AboveMaximum));
        }
    }
}

public sealed class ContentServiceSentValueTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task<(Guid Id, Principal Writer)> TooLongStoredTitleAsync()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "T", ["seo"] = new ContentDocument { ["metaTitle"] = "short" } }));
        _database.Read(context => context.Rows("cms_article").Where(row => EF.Property<Guid>(row, "document_id") == id).ExecuteUpdate(set => set.SetProperty(row => EF.Property<string?>(row, "seo_meta_title"), "a stored value much too long")));
        var role = _database.Database.AddRoleForTest("WriteOnly", ("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.article.update", null));
        return (id, WithRole(role));
    }

    public static TheoryData<object> PatchesOfAnotherFieldOfTheComponent() => new()
    {
        new ContentDocument { ["image"] = Guid.Parse("0192f2a4-3333-7000-8000-000000000003") },
        JsonNode.Parse("""{"image":"0192f2a4-3333-7000-8000-000000000003"}""")!,
        """{"image":"0192f2a4-3333-7000-8000-000000000003"}""",
    };

    [Theory]
    [MemberData(nameof(PatchesOfAnotherFieldOfTheComponent))]
    public async Task APatchIntoAComponentDoesNotHearOfAStoredValueItDidNotSend(object seo)
    {
        var (id, writer) = await TooLongStoredTitleAsync();

        var patched = await _database.PatchAsync(writer, id, new() { ["seo"] = seo }, 1);

        patched["version"].ShouldBe(2);
        _database.Row("cms_article", id)["seo_meta_title"].ShouldBe("a stored value much too long");
    }

    [Fact]
    public async Task AValueSentInsideAComponentIsReported()
    {
        var (id, writer) = await TooLongStoredTitleAsync();

        foreach (var guess in new[] { "a stored value much too long", "another value much too long" })
        {
            (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(writer, id, new() { ["seo"] = new ContentDocument { ["metaTitle"] = guess } }, 1)))
                .Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                    error => error.Path.ShouldBe("seo.metaTitle"),
                    error => error.Code.ShouldBe(ValidationErrorCodes.MaxLength));
        }
    }

    [Fact]
    public async Task AValueSentInsideABlockIsReportedAtItsIndex()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "T" }));
        var role = _database.Database.AddRoleForTest("WriteOnly", ("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.article.update", null));

        (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(WithRole(role), id, new() { ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"ok"},{"type":"hero","heading":"much too long a heading"}]""") }, 1)))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("sections[1].heading");
    }
}

public sealed class ContentServiceFallbackEchoTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task<Guid> EnglishAndFrenchAsync()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "English" }));
        await _database.PatchAsync(Editor, id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
        return id;
    }

    [Fact]
    public async Task AnEditToAFieldThatFellBackIsWritten()
    {
        var id = await EnglishAndFrenchAsync();
        var read = (await _database.GetAsync(Editor, id, locale: "fr"))!;
        read.ContainsKey("fallbacks").ShouldBeTrue();
        read["subtitle"] = "Ma traduction";

        var saved = await _database.UpdateAsync(Editor, id, read, 1, locale: "fr");

        saved["subtitle"].ShouldBe("Ma traduction");
        _database.Row("cms_article_i18n", id, "fr")["subtitle"].ShouldBe("Ma traduction");
    }

    [Fact]
    public async Task WhatTheChainServedComesBackAsNotSentWithOrWithoutTheMap()
    {
        var id = await EnglishAndFrenchAsync();
        var read = (await _database.GetAsync(Editor, id, locale: "fr"))!;
        read.Remove("fallbacks");

        await _database.UpdateAsync(Editor, id, read, 1, locale: "fr");

        _database.Row("cms_article_i18n", id, "fr")["subtitle"].ShouldBeNull();
    }

    [Fact]
    public async Task AValueAlreadyInThisLocaleIsKeptEvenWhenEqualToTheChain()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "Same" }));
        await _database.PatchAsync(Editor, id, new() { ["title"] = "Bonjour", ["subtitle"] = "Same" }, 0, locale: "fr");

        var read = (await _database.GetAsync(Editor, id, locale: "fr"))!;
        await _database.UpdateAsync(Editor, id, read, 1, locale: "fr");

        _database.Row("cms_article_i18n", id, "fr")["subtitle"].ShouldBe("Same");
    }

    [Fact]
    public async Task APatchIsExplicitAndWritesTheSameTextAsTheChain()
    {
        var id = await EnglishAndFrenchAsync();

        await _database.PatchAsync(Editor, id, new() { ["subtitle"] = "English" }, 1, locale: "fr");

        _database.Row("cms_article_i18n", id, "fr")["subtitle"].ShouldBe("English");
    }

    [Fact]
    public async Task AddingALocaleFromAFallbackReadWritesOnlyWhatWasTranslated()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "English", ["views"] = 3 }));
        var read = (await _database.GetAsync(Editor, id, locale: "fr"))!;
        read["title"] = "Traduit";

        await _database.UpdateAsync(Editor, id, read, 0, locale: "fr");

        var row = _database.Row("cms_article_i18n", id, "fr");
        row["title"].ShouldBe("Traduit");
        row["subtitle"].ShouldBeNull();
        row["slug"].ShouldBe("traduit");
    }

    [Fact]
    public async Task ATypedRoundTripDoesNotWriteAFallbackIntoTheLocale()
    {
        const string Body = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"English body"}]}]}""";
        using var database = new ServiceDatabase(schema: SampleSchema.FromClasses().Build());
        var created = await database.AsAsync(Editor, services => services.GetRequiredService<IContentService<Article>>().CreateAsync(new Article { Title = "Hello", Body = new RichText(Body) }, Token));
        await database.PatchAsync(Editor, created.Id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");

        await database.AsAsync(
            Editor,
            async services =>
            {
                var service = services.GetRequiredService<IContentService<Article>>();
                var read = (await service.GetAsync(created.Id, new GetOptions { Status = QueryStatus.Draft }, Token))!;
                read.Body.Json.ShouldBe(Body);
                return await service.UpdateAsync(created.Id, read, read.Version, Token);
            },
            locale: "fr");

        database.Row("cms_article_i18n", created.Id, "fr")["body"].ShouldBe(RichText.Empty.Json);
    }
}

public sealed class ContentServiceGrantTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [FluentCMS.Schema.Collection("article")]
    private sealed class Regional : ContentItem
    {
        public string? Region { get; set; }
    }

    [Fact]
    public async Task ACreateIsDecidedPerGrantAgainstTheNewEntry()
    {
        using var database = new ServiceDatabase(cms => cms.Authorization
            .AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region"))
            .AddRowRule<Regional>("never", ctx => article => article.Region == "no-such-region"));
        var role = database.Database.AddRoleForTest(
            "Pair",
            ("content.article.create", new PermissionConstraints { RowRule = "never" }),
            ("content.article.create", new PermissionConstraints { RowRule = "region", Fields = ["title", "region"] }));
        var principal = AuthorizationDatabase.User([role], [TokenScopes.Management], claims: new() { ["region"] = "eu" });
        var before = database.Footprint();

        (await Should.ThrowAsync<ValidationException>(() => database.CreateAsync(principal, new() { ["title"] = "T", ["region"] = "eu", ["views"] = 7 })))
            .Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                error => error.Path.ShouldBe("views"),
                error => error.Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable));
        database.Footprint().ShouldBe(before);

        await Should.NotThrowAsync(() => database.CreateAsync(principal, new() { ["title"] = "T", ["region"] = "eu" }));
    }

    [Fact]
    public async Task ASiteRoleCannotChangeAGlobalEntry()
    {
        var tag = await _database.CreateAsync(Editor, new() { ["name"] = "Shared" }, "tag");
        var trashed = await _database.CreateAsync(Editor, new() { ["name"] = "Trashed" }, "tag");
        await _database.DeleteAsync(Editor, IdOf(trashed), "tag");
        var role = _database.Database.AddRoleForTest(
            "Site A",
            QueryDatabase.SiteA,
            ("content.tag.create", null),
            ("content.tag.read", null),
            ("content.tag.update", null),
            ("content.tag.delete", null),
            ("content.tag.restore", null));
        var inA = WithRole(role);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.CreateAsync(inA, new() { ["name"] = "x" }, "tag"));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.UpdateAsync(inA, IdOf(tag), new() { ["name"] = "x" }, 1, "tag"));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PatchAsync(inA, IdOf(tag), new() { ["name"] = "x" }, 1, "tag"));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.DeleteAsync(inA, IdOf(tag), "tag"));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.RestoreAsync(inA, IdOf(trashed), "tag"));

        // Reading stays open to it.
        (await _database.GetAsync(inA, IdOf(tag), type: "tag"))!["name"].ShouldBe("Shared");
    }

    [Fact]
    public async Task AGrantLimitedToSitesCannotChangeAGlobalEntry()
    {
        var tag = await _database.CreateAsync(Editor, new() { ["name"] = "Shared" }, "tag");
        var role = _database.Database.AddRoleForTest("Limited", ("content.tag.read", null), ("content.tag.update", new PermissionConstraints { Sites = [SiteA] }));

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PatchAsync(WithRole(role), IdOf(tag), new() { ["name"] = "x" }, 1, "tag"));
    }

    [Fact]
    public async Task AnInstanceWideRoleWritesAGlobalEntryFromAnySiteAndASiteRoleItsOwnEntries()
    {
        var tag = await _database.CreateAsync(Editor, new() { ["name"] = "Shared" }, "tag", site: SiteB);
        (await _database.UpdateAsync(Editor, IdOf(tag), new() { ["name"] = "Changed" }, 1, "tag"))["name"].ShouldBe("Changed");
        await _database.DeleteAsync(Editor, IdOf(tag), "tag");
        await _database.RestoreAsync(Editor, IdOf(tag), "tag");

        var author = AuthorRole(Guid.CreateVersion7());
        var own = await _database.CreateAsync(author, new() { ["name"] = "Mine" }, "tag");
        (await _database.PatchAsync(author, IdOf(own), new() { ["name"] = "Still mine" }, 1, "tag"))["name"].ShouldBe("Still mine");

        var role = _database.Database.AddRoleForTest("Site A", QueryDatabase.SiteA, ("content.article.create", null));
        await Should.NotThrowAsync(() => _database.CreateAsync(WithRole(role), new() { ["title"] = "Here" }));
    }

    [FluentCMS.Schema.Collection("scored")]
    public sealed class Scored : ContentItem
    {
        [Text]
        public string Name { get; set; } = "";

        [Integer, Hidden]
        public int Score { get; set; }
    }

    [Fact]
    public async Task ASystemTypedWriteKeepsItsHiddenValues()
    {
        using var database = new ServiceDatabase(schema: SampleSchema.NewBuilder().AddContentType<Scored>().Build());

        var created = await database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService<Scored>>().CreateAsync(new Scored { Name = "seeded", Score = 42 }, Token),
            origin: Origin.Seed);

        database.Row("cms_scored", created.Id)["score"].ShouldBe(42L);
    }

    [Fact]
    public async Task ASystemJobThatReadsAndUpdatesAClassKeepsAHiddenValue()
    {
        using var database = new ServiceDatabase(schema: SampleSchema.NewBuilder().AddContentType<Scored>().Build());
        Task<T> AsJob<T>(Func<IContentService<Scored>, Task<T>> work) =>
            database.AsAsync(Principal.System, services => work(services.GetRequiredService<IContentService<Scored>>()), origin: Origin.Job);
        var created = await AsJob(service => service.CreateAsync(new Scored { Name = "seeded", Score = 42 }, Token));

        var read = (await AsJob(service => service.GetAsync(created.Id, new GetOptions { Status = QueryStatus.Draft }, Token)))!;
        read.Score.ShouldBe(0);
        read.Name = "changed";
        await AsJob(service => service.UpdateAsync(created.Id, read, read.Version, Token));

        var row = database.Row("cms_scored", created.Id);
        row["name"].ShouldBe("changed");
        row["score"].ShouldBe(42L);

        // A value other than the default is a write, and a patch puts it back to the default.
        read.Score = 50;
        await AsJob(service => service.UpdateAsync(created.Id, read, 2, Token));
        database.Row("cms_scored", created.Id)["score"].ShouldBe(50L);

        await AsJob(service => service.PatchAsync(created.Id, new() { ["score"] = 0 }, 3, Token));
        database.Row("cms_scored", created.Id)["score"].ShouldBe(0L);
    }

    [Fact]
    public async Task ASystemTypedCreateSendsAHiddenDefaultToo()
    {
        using var database = new ServiceDatabase(schema: SampleSchema.NewBuilder().AddContentType<Scored>().Build());

        var created = await database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService<Scored>>().CreateAsync(new Scored { Name = "seeded" }, Token),
            origin: Origin.Seed);

        database.Row("cms_scored", created.Id)["score"].ShouldBe(0L);
    }
}

public sealed class ContentServiceRelationKeepTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private List<Guid> TagsOf(Guid id) => [.. _database.References(id).Where(row => row.Field == "tags").OrderBy(row => row.Position).Select(row => row.TargetId)];

    private async Task<(Principal Ann, Guid Hidden, Guid Mine, Guid Entry)> AnnWithAHiddenTagAsync()
    {
        var ann = AuthorRole(Guid.CreateVersion7());
        var hidden = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Theirs" }, "tag"));
        var mine = IdOf(await _database.CreateAsync(ann, new() { ["name"] = "Mine" }, "tag"));
        var entry = IdOf(await _database.CreateAsync(ann, new() { ["title"] = "T", ["tags"] = new[] { hidden, mine } }));
        return (ann, hidden, mine, entry);
    }

    [Fact]
    public async Task ATitleOnlyRoundTripLeavesARelationAndTheOtherLocalesAlone()
    {
        var (ann, hidden, mine, id) = await AnnWithAHiddenTagAsync();
        await _database.PatchAsync(Editor, id, new() { ["title"] = "Fr" }, 0, locale: "fr");
        _database.Read(context => context.Documents.Where(document => document.DocumentId == id).ExecuteUpdate(set => set.SetProperty(document => document.Status, EntryStatus.Published)));

        var read = (await _database.GetAsync(ann, id))!;
        read["title"] = "Only the title";
        await _database.UpdateAsync(ann, id, read, 1);

        TagsOf(id).ShouldBe([hidden, mine]);
        _database.Read(context => context.Documents.AsNoTracking().Single(document => document.DocumentId == id && document.Locale == "fr").Status).ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task AKeptTargetGoesBackToItsPlaceClamped()
    {
        var (ann, hidden, mine, id) = await AnnWithAHiddenTagAsync();
        var other = IdOf(await _database.CreateAsync(ann, new() { ["name"] = "Other" }, "tag"));

        await _database.UpdateAsync(ann, id, new() { ["title"] = "T", ["tags"] = new[] { other, mine } }, 1);
        TagsOf(id).ShouldBe([hidden, other, mine]);
    }

    [Fact]
    public async Task APatchCannotRemoveATargetThePrincipalCannotSee()
    {
        var (ann, hidden, mine, id) = await AnnWithAHiddenTagAsync();

        await _database.PatchAsync(ann, id, new() { ["tags"] = new[] { mine } }, 1);
        TagsOf(id).ShouldBe([hidden, mine]);

        await _database.PatchAsync(ann, id, new() { ["tags"] = Array.Empty<Guid>() }, 2);
        TagsOf(id).ShouldBe([hidden]);
    }

    [Fact]
    public async Task AGhostIdIsClearedByAFullUpdateFromAnyone()
    {
        var ghost = Guid.CreateVersion7();
        var first = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "G", ["author"] = ghost, ["tags"] = new[] { ghost } }));

        await _database.UpdateAsync(Editor, first, new() { ["title"] = "G" }, 1);

        _database.References(first).ShouldBeEmpty();
    }

    [Fact]
    public async Task ATrashedTargetTheEditorCanStillReadIsClearedAndOneThePrincipalCannotReadIsKept()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var entry = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "T", ["author"] = author }));
        await _database.DeleteAsync(Editor, author, "author");

        var role = _database.Database.AddRoleForTest("Articles", ("content.article.read", null), ("content.article.update", null));
        await _database.UpdateAsync(WithRole(role), entry, new() { ["title"] = "T", ["author"] = null }, 1);
        _database.References(entry).ShouldHaveSingleItem().TargetId.ShouldBe(author);

        await _database.UpdateAsync(Editor, entry, new() { ["title"] = "T", ["author"] = null }, 2);
        _database.References(entry).ShouldBeEmpty();
    }

    [Fact]
    public async Task TheSystemPrincipalKeepsNothingItDidNotSend()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var inOtherSite = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Bob" }, "author", site: SiteB));
        var entry = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "T", ["author"] = inOtherSite, ["sponsor"] = author }));

        await _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().UpdateAsync("article", entry, new() { ["title"] = "T" }, 1, Token),
            origin: Origin.Job);

        _database.References(entry).ShouldBeEmpty();
    }

    [Fact]
    public async Task LeftoverBlocksPairWithinTheirType()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "T" }));
        await _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().PatchAsync("article", id, new() { ["sections"] = JsonNode.Parse("""[{"type":"hero","heading":"A","internal":"hA"},{"type":"quote","text":"q"},{"type":"hero","heading":"B","internal":"hB"}]""") }, 1, Token),
            origin: Origin.Job);

        await _database.PatchAsync(Editor, id, new() { ["sections"] = JsonNode.Parse("""[{"type":"quote","text":"q2"},{"type":"hero","heading":"B2"}]""") }, 2);

        var sections = JsonNode.Parse((string)_database.Row("cms_article", id)["sections"])!;
        sections[0]!["internal"].ShouldBeNull();
        sections[1]!["heading"]!.GetValue<string>().ShouldBe("B2");
        sections[1]!["internal"]!.GetValue<string>().ShouldBe("hA");
    }
}

public sealed class ContentServiceJoinedTransactionTests
{
    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    [FluentCMS.Schema.Collection("article")]
    private sealed class Regional : ContentItem
    {
        public string? Region { get; set; }
    }

    [Fact]
    public async Task AFailedWriteLeavesTheCallersUnsavedWorkForItToSave()
    {
        using var database = new ServiceDatabase(cms => cms.Authorization.AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region")));
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "T", ["region"] = "eu" }));
        var keep = database.Database.AddRoleForTest("Existing");
        var role = database.Database.AddRoleForTest("Regional", ("content.article.read", new PermissionConstraints { RowRule = "region" }), ("content.article.update", new PermissionConstraints { RowRule = "region" }));
        var regional = AuthorizationDatabase.User([role], [TokenScopes.Management], claims: new() { ["region"] = "eu" });

        await database.AsAsync(regional, async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            var service = services.GetRequiredService<IContentService>();
            await using var transaction = await context.Database.BeginTransactionAsync(Token);
            context.Add(new Role { Name = "New from the caller" });
            var existing = await context.Set<Role>().SingleAsync(item => item.Id == keep, Token);
            existing.Description = "Changed by the caller";

            // One that fails before it writes, and one that fails after: its rows are saved, then refused.
            await Should.ThrowAsync<VersionConflictException>(() => service.PatchAsync("article", id, Titled("x"), 99, Token));
            await Should.ThrowAsync<ContentAccessDeniedException>(() => service.PatchAsync("article", id, new() { ["region"] = "us" }, 1, Token));

            context.ChangeTracker.Entries<Role>().Count(entry => entry.State == EntityState.Added).ShouldBe(1);
            context.ChangeTracker.Entries<Role>().Count(entry => entry.State == EntityState.Modified).ShouldBe(1);

            await context.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
            return true;
        });

        database.Read(context => context.Set<Role>().Count(item => item.Name == "New from the caller")).ShouldBe(1);
        database.Read(context => context.Set<Role>().Single(item => item.Id == keep).Description).ShouldBe("Changed by the caller");
        (await database.GetAsync(Editor, id))!["region"].ShouldBe("eu");
    }

    [Fact]
    public async Task ASuccessfulWriteDoesNotSaveTheCallersWorkEither()
    {
        using var database = new ServiceDatabase();
        var id = IdOf(await database.CreateAsync(Editor, Titled("T")));

        await database.AsAsync(Editor, async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(Token);
            context.Add(new Role { Name = "Not saved" });

            await services.GetRequiredService<IContentService>().PatchAsync("article", id, Titled("Changed"), 1, Token);

            context.ChangeTracker.Entries<Role>().ShouldHaveSingleItem().State.ShouldBe(EntityState.Added);
            await transaction.CommitAsync(Token);
            return true;
        });

        database.Read(context => context.Set<Role>().Count(item => item.Name == "Not saved")).ShouldBe(0);
        (await database.GetAsync(Editor, id))!["title"].ShouldBe("Changed");
    }

    private sealed class NoSavepoints(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid id,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool owned,
        ISqlGenerationHelper helper)
        : RelationalTransaction(connection, transaction, id, logger, owned, helper)
    {
        public override bool SupportsSavepoints => false;
    }

    private sealed class NoSavepointsFactory(RelationalTransactionFactoryDependencies dependencies) : IRelationalTransactionFactory
    {
        public RelationalTransaction Create(IRelationalConnection connection, DbTransaction transaction, Guid transactionId, IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned) =>
            new NoSavepoints(connection, transaction, transactionId, logger, transactionOwned, dependencies.SqlGenerationHelper);
    }

    [Fact]
    public async Task WithoutSavepointsAFailedJoinedWriteRollsTheOuterTransactionBack()
    {
        using var database = new ServiceDatabase(cms => cms.Services.ConfigureDbContext<CmsDbContext>(options => options.ReplaceService<IRelationalTransactionFactory, NoSavepointsFactory>()));
        var before = database.Footprint();

        await database.AsAsync(Editor, async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            var service = services.GetRequiredService<IContentService>();
            var transaction = await context.Database.BeginTransactionAsync(Token);
            await using (transaction)
            {
                transaction.SupportsSavepoints.ShouldBeFalse();
                await service.CreateAsync("article", Titled("One"), Token);
                await Should.ThrowAsync<ValidationException>(() => service.CreateAsync("article", new() { ["views"] = 5000 }, Token));

                // As a hook that caught the failure would carry on: the write must not commit outside the transaction.
                await Should.ThrowAsync<InvalidOperationException>(() => service.CreateAsync("article", Titled("Two"), Token));
                await Should.ThrowAsync<InvalidOperationException>(() => transaction.CommitAsync(Token));
            }

            return true;
        });

        database.Footprint().ShouldBe(before);
    }

    // The test above stands in for a SQL Server connection with MARS, on which EF takes no savepoints.
    [SqlServerFact("MARS, with which EF takes no savepoints")]
    public async Task OnSqlServerWithMarsATransactionHasNoSavepoints()
    {
        using var database = TestDatabase.Create();
        var mars = new SqlConnectionStringBuilder(database.ConnectionString) { MultipleActiveResultSets = true }.ConnectionString;
        await using var context = new DbContext(new DbContextOptionsBuilder().UseSqlServer(mars).Options);

        await using var transaction = await context.Database.BeginTransactionAsync(Token);

        transaction.SupportsSavepoints.ShouldBeFalse();
    }

    [Fact]
    public async Task AFailedBeginDoesNotLeaveTheServiceOwningATransaction()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var recorder = new Recorder();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<FluentCMS.Core.Content.IEntryChangeSink>(recorder));

        await database.AsAsync(Editor, async services =>
        {
            var service = services.GetRequiredService<IContentService>();
            await Should.ThrowAsync<OperationCanceledException>(() => service.CreateAsync("article", Titled("One"), source.Token));

            await using var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token);
            await service.CreateAsync("article", Titled("In a foreign transaction"), Token);
            await transaction.RollbackAsync(Token);
            await service.CreateAsync("article", Titled("Own"), Token);
            return true;
        });

        recorder.Changes.ShouldHaveSingleItem();
    }

    private sealed class Recorder : FluentCMS.Core.Content.IEntryChangeSink
    {
        public List<FluentCMS.Core.Content.EntryChange> Changes { get; } = [];

        public ValueTask PublishAsync(FluentCMS.Core.Content.EntryChange change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AddingALocaleWorksWhenTheOldestLocaleIsDisabled()
    {
        using var database = new ServiceDatabase();
        var id = IdOf(await database.CreateAsync(Editor, Titled("Bonjour"), locale: "fr"));
        database.Read(context => context.Set<SiteLocale>().Where(locale => locale.Code == "fr").ExecuteUpdate(set => set.SetProperty(locale => locale.Enabled, false)));

        (await database.UpdateAsync(Editor, id, Titled("Hello"), 0, locale: "en"))["version"].ShouldBe(1);
        (await database.PatchAsync(Editor, id, Titled("Salut"), 0, locale: "fr-CA"))["version"].ShouldBe(1);
    }
}
