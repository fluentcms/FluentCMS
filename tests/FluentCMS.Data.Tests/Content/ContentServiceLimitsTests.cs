using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Query;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// What a principal cannot read, and may not reach, staying that way through every path.
public sealed class ContentServiceLimitsTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    [FluentCMS.Schema.Collection("article")]
    private sealed class Regional : ContentItem
    {
        public string? Region { get; set; }
    }

    private static ServiceDatabase WithRegionRule() =>
        new(cms => cms.Authorization.AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region")));

    private static Principal InRegion(Guid role, string region) =>
        AuthorizationDatabase.User([role], [TokenScopes.Management], claims: new() { ["region"] = region });

    [Fact]
    public async Task AQueryCannotNameAnotherSite()
    {
        await _database.CreateAsync(Editor, Titled("In A"));
        await _database.CreateAsync(Editor, Titled("In B"), site: SiteB);
        _database.Database.AddArticle("Published in B", site: QueryDatabase.SiteB);
        _database.Database.GrantForTest(BuiltInRoles.Public, "content.article.read");
        _database.Database.AddRoleForTest("Public", QueryDatabase.SiteB);

        foreach (var (principal, status) in new[] { (Editor, QueryStatus.Draft), (Principal.Anonymous, QueryStatus.Published) })
        {
            var refused = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(principal, new ContentQuery { Status = status, Site = SiteB }));
            refused.Errors.ShouldHaveSingleItem().Path.ShouldBe("site");
        }

        // In its own site the anonymous principal gets that site's empty Public role.
        await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(Principal.Anonymous, new ContentQuery(), site: SiteB));
        (await _database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Draft, Site = SiteA })).Items.ShouldHaveSingleItem()["title"].ShouldBe("In A");
    }

    [Fact]
    public async Task TheSystemPrincipalMayNameAnotherSite()
    {
        await _database.CreateAsync(Editor, Titled("In B"), site: SiteB);

        var page = await _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().QueryAsync("article", new ContentQuery { Status = QueryStatus.Draft, Site = SiteB }, Token),
            origin: Origin.Job);

        page.Items.ShouldHaveSingleItem()["title"].ShouldBe("In B");
    }

    [Fact]
    public async Task ASlugDoesNotShowASourceThePrincipalCannotRead()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Top Secret Plan", ["slug"] = "public-slug" });
        var role = _database.Database.AddRoleForTest("Slugs", ("content.article.read", new PermissionConstraints { Fields = ["slug", "subtitle"] }), ("content.article.update", new PermissionConstraints { Fields = ["slug"] }));

        var patched = await _database.PatchAsync(WithRole(role), IdOf(entry), new() { ["slug"] = "" }, 1);

        patched["slug"].ShouldBe("public-slug");
        _database.Row("cms_article_i18n", IdOf(entry), "en")["slug"].ShouldBe("public-slug");

        // A principal that may read the source still gets a slug from it.
        (await _database.PatchAsync(Editor, IdOf(entry), new() { ["slug"] = "" }, 2))["slug"].ShouldBe("top-secret-plan");
    }

    [Fact]
    public async Task ASlugIsNotMadeFromAHiddenSource()
    {
        var schema = SampleSchema.NewBuilder()
            .AddCollection("page", type => type
                .Field("code", "text", field => field.Hidden())
                .Field("slug", "slug", field => field.Setting(FieldSettings.Source, "code")))
            .Build();
        using var database = new ServiceDatabase(schema: schema);

        var entry = await database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().CreateAsync("page", new() { ["code"] = "Secret Code" }, Token),
            origin: Origin.Seed);

        entry["slug"].ShouldBe("");
    }

    [Fact]
    public async Task AGrantLimitedToALocaleDoesNotReachSharedFields()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 5, ["region"] = "eu" });
        var id = IdOf(entry);
        await _database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour", ["views"] = 5, ["region"] = "eu" }, 0, locale: "fr");
        var role = _database.Database.AddRoleForTest("Translator", ("content.article.update", new PermissionConstraints { Locales = ["fr"] }), ("content.article.read", null));
        var translator = WithRole(role);

        (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(translator, id, new() { ["views"] = 999 }, 1, locale: "fr")))
            .Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                error => error.Path.ShouldBe("views"),
                error => error.Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable));

        // A full update that leaves them out keeps them, and one that sends them back unchanged is no write.
        await _database.UpdateAsync(translator, id, Titled("Salut"), 1, locale: "fr");
        var read = (await _database.GetAsync(translator, id, locale: "fr"))!;
        read["title"] = "Salut encore";
        await _database.UpdateAsync(translator, id, read, 2, locale: "fr");

        var english = (await _database.GetAsync(Editor, id))!;
        english["views"].ShouldBe(5L);
        english["region"].ShouldBe("eu");
        (await _database.GetAsync(Editor, id, locale: "fr"))!["title"].ShouldBe("Salut encore");
    }

    [Fact]
    public async Task AGrantCoveringEveryLocaleReachesSharedFields()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 5 });
        var id = IdOf(entry);
        await _database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        var role = _database.Database.AddRoleForTest("Both", ("content.article.update", new PermissionConstraints { Locales = ["fr", "en"] }), ("content.article.read", null));

        (await _database.PatchAsync(WithRole(role), id, new() { ["views"] = 6 }, 1, locale: "fr"))["views"].ShouldBe(6L);

        // Adding a locale it does not cover leaves it unable to change what that locale will share too.
        await _database.UpdateAsync(Editor, id, Titled("Salut"), 0, locale: "fr-CA");
        await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(WithRole(role), id, new() { ["views"] = 7 }, 2, locale: "fr"));
    }

    [Fact]
    public async Task AGrantLimitedToALocaleAddsThatLocaleWithoutTheSharedFields()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 5 });
        var role = _database.Database.AddRoleForTest("Translator", ("content.article.update", new PermissionConstraints { Locales = ["fr"] }), ("content.article.read", null));

        await Should.ThrowAsync<ValidationException>(() => _database.UpdateAsync(WithRole(role), IdOf(entry), new() { ["title"] = "Bonjour", ["views"] = 9 }, 0, locale: "fr"));
        (await _database.UpdateAsync(WithRole(role), IdOf(entry), Titled("Bonjour"), 0, locale: "fr"))["views"].ShouldBe(5L);
    }

    [Fact]
    public async Task CreateHonoursTheRowRuleOfItsGrant()
    {
        using var database = WithRegionRule();
        var role = database.Database.AddRoleForTest(
            "Regional",
            ("content.article.create", new PermissionConstraints { RowRule = "region" }),
            ("content.article.read", new PermissionConstraints { RowRule = "region" }));
        var before = database.Footprint();

        await Should.ThrowAsync<ContentAccessDeniedException>(() => database.CreateAsync(InRegion(role, "eu"), new() { ["title"] = "T", ["region"] = "us" }));
        database.Footprint().ShouldBe(before);

        (await database.CreateAsync(InRegion(role, "eu"), new() { ["title"] = "T", ["region"] = "eu" }))["region"].ShouldBe("eu");
    }

    [Fact]
    public async Task ARowThePrincipalCannotReadIsNoWayToGuessItsValues()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "T", ["views"] = 5 });
        var role = _database.Database.AddRoleForTest(
            "Own",
            ("content.article.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }),
            ("content.article.update", new PermissionConstraints { Fields = ["title"] }));
        var principal = WithRole(role, Guid.CreateVersion7());

        (await _database.GetAsync(principal, IdOf(entry))).ShouldBeNull();
        foreach (var guess in new[] { 5, 6 })
        {
            (await Should.ThrowAsync<ValidationException>(() => _database.UpdateAsync(principal, IdOf(entry), new() { ["title"] = "T", ["views"] = guess }, 1)))
                .Errors.ShouldHaveSingleItem().Path.ShouldBe("views");
        }
    }

    [Fact]
    public async Task ATargetThePrincipalCannotReadIsNoWayToGuessARelation()
    {
        var ann = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "T", ["author"] = ann });
        var role = _database.Database.AddRoleForTest("Articles", ("content.article.read", null), ("content.article.update", new PermissionConstraints { Fields = ["title"] }));
        var limited = WithRole(role);

        (await _database.GetAsync(limited, IdOf(entry)))!["author"].ShouldBeNull();
        foreach (var guess in new[] { ann, Guid.CreateVersion7() })
        {
            (await Should.ThrowAsync<ValidationException>(() => _database.UpdateAsync(limited, IdOf(entry), new() { ["title"] = "T", ["author"] = guess }, 1)))
                .Errors.ShouldHaveSingleItem().Path.ShouldBe("author");
        }

        // What the read showed goes back as no change.
        var read = (await _database.GetAsync(limited, IdOf(entry)))!;
        await _database.UpdateAsync(limited, IdOf(entry), read, 1);
        _database.References(IdOf(entry)).ShouldHaveSingleItem().TargetId.ShouldBe(ann);
    }

    [Fact]
    public async Task AnErrorOnAFieldThePrincipalCannotReadAndDidNotChangeNeitherBlocksNorShows()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "T", ["views"] = 5 });
        var id = IdOf(entry);
        _database.Read(context => context.Rows("cms_article").Where(row => EF.Property<Guid>(row, "document_id") == id).ExecuteUpdate(set => set.SetProperty(row => EF.Property<long?>(row, "views"), 5000L)));
        var role = _database.Database.AddRoleForTest("Titles", ("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.article.update", new PermissionConstraints { Fields = ["title"] }));

        (await _database.PatchAsync(WithRole(role), id, Titled("Changed"), 1))["title"].ShouldBe("Changed");

        // For someone who can read the field it is an error like any other.
        (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(Editor, id, Titled("Again"), 2)))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("views");
    }

    [Fact]
    public async Task UniquenessStillRunsAfterAnErrorThatDoesNotCount()
    {
        await _database.CreateAsync(Editor, new() { ["title"] = "Taken" });
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "T", ["views"] = 5 });
        var id = IdOf(entry);
        _database.Read(context => context.Rows("cms_article").Where(row => EF.Property<Guid>(row, "document_id") == id).ExecuteUpdate(set => set.SetProperty(row => EF.Property<long?>(row, "views"), 5000L)));
        var role = _database.Database.AddRoleForTest("Titles", ("content.article.read", new PermissionConstraints { Fields = ["title", "slug"] }), ("content.article.update", new PermissionConstraints { Fields = ["title", "slug"] }));

        (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(WithRole(role), id, new() { ["slug"] = "taken" }, 1)))
            .Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                error => error.Path.ShouldBe("slug"),
                error => error.Code.ShouldBe(ValidationErrorCodes.NotUnique));
    }

    private sealed class SetViews : IBeforeUpdateHook<ContentDocument>
    {
        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry["views"] = 5000;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AHookThatMakesAnUnreadableFieldInvalidFailsWithoutNamingIt()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeUpdateHook<ContentDocument>, SetViews>());
        var entry = await database.CreateAsync(Editor, Titled("T"));
        var role = database.Database.AddRoleForTest("Writer", ("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.article.update", null));

        var refused = await Should.ThrowAsync<ValidationException>(() => database.PatchAsync(WithRole(role), IdOf(entry), Titled("Changed"), 1));

        var error = refused.Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("");
        error.Code.ShouldBe(ValidationErrorCodes.InvalidValue);
        error.Message.ShouldNotContain("views");
        error.Message.ShouldNotContain("5000");
        error.Message.ShouldNotContain("1000");
    }

    [Fact]
    public async Task AnInvalidValueAClientSentIsReportedEvenWhenItCannotReadTheField()
    {
        var entry = await _database.CreateAsync(Editor, Titled("T"));
        var role = _database.Database.AddRoleForTest("Writer", ("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.article.update", null));

        (await Should.ThrowAsync<ValidationException>(() => _database.PatchAsync(WithRole(role), IdOf(entry), new() { ["views"] = 5000 }, 1)))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("views");
    }

    private sealed class ChangePrevious : IBeforeUpdateHook<ContentDocument>
    {
        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Previous!["views"] = context.Entry["views"];
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AHookCannotChangeTheStoredStateTheWriteComparesWith()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeUpdateHook<ContentDocument>, ChangePrevious>());
        var entry = await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 });
        var id = IdOf(entry);
        await database.PatchAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        await database.PublishAsync(Editor, id);
        await database.PublishAsync(Editor, id, "fr");

        await database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 1);

        database.Read(context => context.Documents.AsNoTracking().Single(document => document.DocumentId == id && document.Locale == "fr").Status).ShouldBe(EntryStatus.Modified);
    }

    [Fact]
    public void FallbacksIsAReservedFieldName()
    {
        Should.Throw<SchemaValidationException>(() => SampleSchema.NewBuilder().AddCollection("page", type => type.Field("fallbacks", "text")).Build());
    }

    [Fact]
    public async Task WhatAFallbackServedDoesNotComeBackAsAValue()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "English" });
        var id = IdOf(entry);
        await _database.PatchAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");

        var read = (await _database.GetAsync(Editor, id, locale: "fr"))!;
        read["subtitle"].ShouldBe("English");
        read.ContainsKey("fallbacks").ShouldBeTrue();

        var saved = await _database.UpdateAsync(Editor, id, read, 1, locale: "fr");

        saved["version"].ShouldBe(2);
        _database.Row("cms_article_i18n", id, "fr")["subtitle"].ShouldBeNull();
        JsonNode.Parse(_database.Versions(id).Last(version => version.Locale == "fr").Snapshot)!["subtitle"].ShouldBeNull();
    }

    [Fact]
    public async Task APatchIgnoresFallbacksAndKeepsTheFieldsItNames()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "Sub" });

        var patched = await _database.PatchAsync(Editor, IdOf(entry), new()
        {
            ["fallbacks"] = new ContentDocument { ["subtitle"] = "fr" },
            ["views"] = 3,
        }, 1);

        patched["subtitle"].ShouldBe("Sub");
        patched["views"].ShouldBe(3L);
    }
}
