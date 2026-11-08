using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// Publishing and versions as a principal with little: what it may not read stays out of every answer, and what it may
// not write stays as it was, on every transition.
public sealed class PublishingAccessTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    private Principal Limited(params (string Permission, PermissionConstraints? Constraints)[] grants) =>
        WithRole(_database.Database.AddRoleForTest(Guid.NewGuid().ToString("N"), grants));

    private static ContentDocument Secretive(string title, string secret, int views = 1, string heading = "Hi", string inner = "hidden-in-block") => new()
    {
        ["title"] = title,
        ["secret"] = secret,
        ["views"] = views,
        ["seo"] = new ContentDocument { ["metaTitle"] = "Meta", ["internal"] = "hidden-in-group" },
        ["sections"] = new object[] { new ContentDocument { ["type"] = "hero", ["heading"] = heading, ["internal"] = inner } },
    };

    private async Task<Guid> SecretiveEntryAsync()
    {
        var id = IdOf(await _database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().CreateAsync("article", Secretive("One", "s1"), Token), origin: Origin.Seed));
        await _database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().UpdateAsync("article", id, Secretive("Two", "s2", 2, "Hi2", "hidden-2"), 1, Token), origin: Origin.Seed);
        return id;
    }

    [Fact]
    public async Task HiddenValuesAreInTheSnapshotAndInNoVersionReadOrDiffForAnyone()
    {
        var id = await SecretiveEntryAsync();
        _database.Versions(id)[0].Snapshot.ShouldContain("hidden-in-block");

        foreach (var principal in new[] { Editor, Principal.System })
        {
            var origin = principal.IsSystem ? Origin.Seed : Origin.Api;
            var version = await _database.AsAsync(principal, services => services.GetRequiredService<IContentService>().GetVersionAsync("article", id, 1, null, Token), origin: origin);
            var changes = await _database.AsAsync(principal, services => services.GetRequiredService<IContentService>().DiffAsync("article", id, 1, 2, null, Token), origin: origin);

            version!["title"].ShouldBe("One");
            version.ContainsKey("secret").ShouldBeFalse();
            var all = Json(version) + Json(changes.Select(change => (change.Path, Before: change.Before?.ToJsonString(), After: change.After?.ToJsonString())).ToList());
            all.ShouldNotContain("hidden-in-block");
            all.ShouldNotContain("hidden-2");
            all.ShouldNotContain("hidden-in-group");
            all.ShouldNotContain("s1");
            changes.Select(change => change.Path).ShouldContain("sections[0].heading");
            changes.Select(change => change.Path).ShouldNotContain("secret");
            changes.Select(change => change.Path).ShouldNotContain("sections[0].internal");
        }
    }

    [Fact]
    public async Task AFieldItMayNotReadIsInNoVersionReadOrDiff()
    {
        var id = await SecretiveEntryAsync();
        var limited = Limited(("content.article.read", new PermissionConstraints { Fields = ["title"] }));

        var version = (await _database.VersionAsync(limited, id, 1))!;
        version["title"].ShouldBe("One");
        version.ContainsKey("views").ShouldBeFalse();
        version.ContainsKey("sections").ShouldBeFalse();
        version.ContainsKey("seo").ShouldBeFalse();

        (await _database.DiffAsync(limited, id, 1, 2)).ShouldHaveSingleItem().Path.ShouldBe("title");
    }

    [Fact]
    public async Task AChangeOnlyInWhatItMayNotReadIsNoDifference()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["views"] = 1 }));
        await _database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 1);
        var limited = Limited(("content.article.read", new PermissionConstraints { Fields = ["title"] }));

        (await _database.DiffAsync(limited, id, 1, 2)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ARelationTargetItMayNotReadIsNotNamedByAVersionOrADiff()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var tag = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "net" }, "tag"));
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["author"] = author, ["tags"] = new[] { tag } }));
        await _database.PatchAsync(Editor, id, new() { ["author"] = null, ["tags"] = Array.Empty<Guid>() }, 1);

        var limited = Limited(("content.article.read", null), ("content.tag.read", null));
        var version = (await _database.VersionAsync(limited, id, 1))!;
        version["author"].ShouldBeNull();
        var listed = version["tags"].ShouldBeOfType<List<ContentDocument>>().ShouldHaveSingleItem();
        listed["id"].ShouldBe(tag);

        var changes = await _database.DiffAsync(limited, id, 1, 2);
        changes.ShouldHaveSingleItem().Path.ShouldBe("tags[0]");
        Json(changes[0].Before?.ToJsonString()).ShouldNotContain(author.ToString());
    }

    [Fact]
    public async Task ATokenThatOnlyReadsCannotReadVersionsEvenWithAPreviewKey()
    {
        var id = await SecretiveEntryAsync();
        var role = _database.Database.AddRoleForTest("Reader", ("content.article.read", null));

        foreach (var scopes in new[] { Array.Empty<string>(), [TokenScopes.Delivery], [TokenScopes.Draft], [TokenScopes.Preview] })
        {
            var token = AuthorizationDatabase.User([role], scopes);
            await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.VersionsAsync(token, id));
            await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.VersionAsync(token, id, 1));
            await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.DiffAsync(token, id, 1, 2));
        }

        (await Should.ThrowAsync<ValidationException>(() => _database.VersionsAsync(Principal.Anonymous, id))).Errors.ShouldHaveSingleItem().Path.ShouldBe("type");
    }

    [Fact]
    public async Task NoTransitionWorksForATokenWithoutTheManagementScope()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("One")));
        var role = _database.Database.AddRoleForTest("Everything", ("content.article.*", null));
        var token = AuthorizationDatabase.User([role], [TokenScopes.Draft]);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PublishAsync(token, id));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.UnpublishAsync(token, id));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.DiscardAsync(token, id));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.RestoreVersionAsync(token, id, 1));
    }

    [Fact]
    public async Task EachTransitionNeedsItsOwnPermission()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("One")));
        await _database.PublishAsync(Editor, id);
        await _database.PatchAsync(Editor, id, Titled("Two"), 1);
        var before = _database.Trace(id);

        var updater = Limited(("content.article.update", null), ("content.article.read", null));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PublishAsync(updater, id));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.UnpublishAsync(updater, id));

        var publisher = Limited(("content.article.publish", null), ("content.article.unpublish", null), ("content.article.restore", null));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.DiscardAsync(publisher, id));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.RestoreVersionAsync(publisher, id, 1));
        await Should.ThrowAsync<ValidationException>(() => _database.VersionsAsync(publisher, id));

        _database.Trace(id).ShouldBe(before);

        (await _database.PublishAsync(publisher, id)).Entry.Keys.ShouldBe(["id", "version"]);
        (await _database.UnpublishAsync(publisher, id)).Keys.ShouldBe(["id", "version"]);
    }

    [Fact]
    public async Task ARowRuleHoldsOnEveryTransitionAndReadsAsNoEntry()
    {
        var ann = Guid.CreateVersion7();
        var own = new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule };
        var role = _database.Database.AddRoleForTest(
            "Own",
            ("content.article.create", null),
            ("content.article.read", own),
            ("content.article.update", own),
            ("content.article.publish", own),
            ("content.article.unpublish", own));
        var mine = WithRole(role, ann);
        var id = IdOf(await _database.CreateAsync(Editor, Titled("Theirs")));
        await _database.PublishAsync(Editor, id);
        await _database.PatchAsync(Editor, id, Titled("Theirs 2"), 1);
        var before = _database.Trace(id);

        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PublishAsync(mine, id));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.UnpublishAsync(mine, id));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.DiscardAsync(mine, id));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.RestoreVersionAsync(mine, id, 1));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.VersionsAsync(mine, id));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.VersionAsync(mine, id, 1));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.DiffAsync(mine, id, 1, 2));
        _database.Trace(id).ShouldBe(before);

        var nowhere = Guid.CreateVersion7();
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PublishAsync(mine, nowhere));

        var own2 = IdOf(await _database.CreateAsync(mine, Titled("Mine")));
        await Should.NotThrowAsync(() => _database.PublishAsync(mine, own2));
        await Should.NotThrowAsync(() => _database.VersionsAsync(mine, own2));
    }

    [FluentCMS.Schema.Collection("article")]
    private sealed class Regional : ContentItem
    {
        public string? Region { get; set; }
    }

    private sealed class MoveToUs : IBeforePublishHook<ContentDocument>
    {
        public ValueTask BeforePublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry["region"] = "us";
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task APublishHookThatTakesTheEntryOutOfTheRowRuleIsRolledBack()
    {
        using var database = new ServiceDatabase(cms =>
        {
            cms.Authorization.AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region"));
            cms.Services.AddSingleton<IBeforePublishHook<ContentDocument>, MoveToUs>();
        });
        var inRegion = new PermissionConstraints { RowRule = "region" };
        var role = database.Database.AddRoleForTest("Regional", ("content.article.read", inRegion), ("content.article.publish", inRegion));
        var eu = AuthorizationDatabase.User([role], [TokenScopes.Management], claims: new() { ["region"] = "eu" });
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "One", ["region"] = "eu" }));
        var before = database.Trace(id);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => database.PublishAsync(eu, id));

        database.Trace(id).ShouldBe(before);
    }

    private sealed class SaveAnotherLocale : IBeforePublishHook<ContentDocument>
    {
        private readonly IServiceProvider _services;

        public SaveAnotherLocale(IServiceProvider services) => _services = services;

        public async ValueTask BeforePublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            var accessor = _services.GetRequiredService<IOperationContextAccessor>();
            var operation = accessor.Current!;
            accessor.Current = operation with { Locale = "fr" };
            try
            {
                await _services.GetRequiredService<IContentService>().PatchAsync("article", (Guid)context.Entry["id"]!, new() { ["views"] = 7 }, 1, cancellationToken);
            }
            finally
            {
                accessor.Current = operation;
            }
        }
    }

    [Fact]
    public async Task AHookThatSavesTheDraftDuringAPublishIsRefusedSoNothingGoesOutUnvalidated()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddScoped<IBeforePublishHook<ContentDocument>, SaveAnotherLocale>());
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "One", ["views"] = 1 }));
        await database.PatchAsync(Editor, id, Titled("Un"), 0, locale: "fr");
        var before = database.Trace(id);

        await Should.ThrowAsync<HookRejectedException>(() => database.PublishAsync(Editor, id));

        database.Trace(id).ShouldBe(before);
    }

    [Fact]
    public async Task AnUnpublishThatLostTheSwapChangesNothing()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("One")));
        await _database.PublishAsync(Editor, id);
        var before = _database.Trace(id);

        var stale = _database.Document(id);
        stale.PublishedVersion = 99;
        await _database.AsAsync(Editor, async services =>
        {
            (await services.GetRequiredService<EntryStore>().MarkUnpublishedAsync(stale, DateTimeOffset.UtcNow, Editor.Id, Token)).ShouldBeFalse();
            return true;
        });

        _database.Trace(id).ShouldBe(before);
    }

    [Fact]
    public async Task AnUnknownTypeAndAnUnreadableOneReadTheSameOnAVersion()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("vault", type => type.Field("name", "text")).AddCollection("page", type => type.Field("name", "text")).Build();
        using var database = new ServiceDatabase(schema: schema);
        var principal = WithRole(database.Database.AddRoleForTest("Pages", ("content.page.read", null)));

        var unreadable = await Should.ThrowAsync<ValidationException>(() => database.VersionsAsync(principal, Guid.CreateVersion7(), type: "vault"));
        var unknown = await Should.ThrowAsync<ValidationException>(() => database.VersionsAsync(principal, Guid.CreateVersion7(), type: "nosuch"));
        var read = await Should.ThrowAsync<ValidationException>(() => database.GetAsync(principal, Guid.CreateVersion7(), type: "vault"));

        unreadable.Errors.ShouldHaveSingleItem().Code.ShouldBe(unknown.Errors.ShouldHaveSingleItem().Code);
        unreadable.Errors[0].Code.ShouldBe(read.Errors.ShouldHaveSingleItem().Code);
        unreadable.Errors[0].Path.ShouldBe("type");
    }

    [Fact]
    public async Task ARestoreOrDiscardThatTakesTheEntryOutOfTheRowRuleIsRolledBack()
    {
        using var database = new ServiceDatabase(cms => cms.Authorization.AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region")));
        var inRegion = new PermissionConstraints { RowRule = "region" };
        var role = database.Database.AddRoleForTest("Regional", ("content.article.read", inRegion), ("content.article.update", inRegion));
        var eu = AuthorizationDatabase.User([role], [TokenScopes.Management], claims: new() { ["region"] = "eu" });

        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "One", ["region"] = "us" }));
        await database.PublishAsync(Editor, id);
        await database.PatchAsync(Editor, id, new() { ["region"] = "eu" }, 1);
        var before = database.Trace(id);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => database.RestoreVersionAsync(eu, id, 1));
        database.Trace(id).ShouldBe(before);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => database.DiscardAsync(eu, id));
        database.Trace(id).ShouldBe(before);
    }

    [Fact]
    public async Task ALocaleLimitHoldsOnEveryTransitionWhicheverWayTheLocaleIsNamed()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("Hello")));
        await _database.PublishAsync(Editor, id);
        await _database.PatchAsync(Editor, id, Titled("Hello 2"), 1);
        var before = _database.Trace(id);
        var french = new PermissionConstraints { Locales = ["fr"] };
        var translator = Limited(
            ("content.article.read", french),
            ("content.article.update", french),
            ("content.article.publish", french),
            ("content.article.unpublish", french));

        foreach (var (locale, operation) in new[] { ((string?)"en", "fr"), (null, "en") })
        {
            await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PublishAsync(translator, id, locale, operationLocale: operation));
            await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.UnpublishAsync(translator, id, locale, operationLocale: operation));
            await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.DiscardAsync(translator, id, locale, operationLocale: operation));
        }

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.RestoreVersionAsync(translator, id, 1, "en"));
        await Should.ThrowAsync<ValidationException>(() => _database.VersionsAsync(translator, id, "en"));
        await Should.ThrowAsync<ValidationException>(() => _database.VersionAsync(translator, id, 1, "en"));
        await Should.ThrowAsync<ValidationException>(() => _database.DiffAsync(translator, id, 1, 2, "en"));
        await Should.ThrowAsync<ValidationException>(() => _database.PublishAsync(translator, id, "all"));

        _database.Trace(id).ShouldBe(before);
    }

    [Fact]
    public async Task AGrantLimitedToALocaleCannotPublishASharedChangeOverTheLocalesItDoesNotCover()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await _database.PatchAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        await _database.PublishAsync(Editor, id);
        var translator = Limited(("content.article.publish", new PermissionConstraints { Locales = ["fr"] }));

        // Nothing shared has moved, so its own locale is its to publish.
        await _database.PublishAsync(translator, id, "fr");
        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Published);

        await _database.PatchAsync(Editor, id, new() { ["views"] = 99 }, 1);
        var before = _database.Trace(id);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PublishAsync(translator, id, "fr"));
        _database.Trace(id).ShouldBe(before);
        (await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Published }))!["views"].ShouldBe(1L);
    }

    [Fact]
    public async Task AGrantLimitedToALocaleMayFirstPublishWhenNoOtherLocaleIsPublished()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await _database.PatchAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        var translator = Limited(("content.article.publish", new PermissionConstraints { Locales = ["fr"] }));

        await _database.PublishAsync(translator, id, "fr");

        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Published);
        _database.Document(id).Status.ShouldBe(EntryStatus.Draft);
    }

    [Fact]
    public async Task AGrantLimitedToSomeFieldsPublishesOnlyWhatStaysWithinThem()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["views"] = 1 }));
        await _database.PublishAsync(Editor, id);
        var titles = Limited(("content.article.publish", new PermissionConstraints { Fields = ["title"] }));

        await _database.PatchAsync(Editor, id, Titled("Two"), 1);
        await _database.PublishAsync(titles, id);
        _database.Document(id).PublishedVersion.ShouldBe(2);

        await _database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 2);
        var before = _database.Trace(id);
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PublishAsync(titles, id));
        _database.Trace(id).ShouldBe(before);
    }

    [Fact]
    public async Task AFieldItCanWriteButNotReadCannotBeGuessedThroughAPublish()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["code"] = "SECRET" }));
        await _database.PublishAsync(Editor, id);
        var guesser = Limited(
            ("content.article.read", new PermissionConstraints { Fields = ["title"] }),
            ("content.article.update", new PermissionConstraints { Fields = ["title", "code"] }),
            ("content.article.publish", new PermissionConstraints { Fields = ["title"] }));

        var version = 1;
        foreach (var guess in new[] { "WRONG", "SECRET" })
        {
            version = VersionOf(await _database.PatchAsync(guesser, id, new() { ["code"] = guess }, version));
            await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PublishAsync(guesser, id));
        }

        // A field it cannot write is not its guess, so the comparison stands.
        await _database.PatchAsync(Editor, id, new() { ["code"] = "SECRET", ["title"] = "Two" }, version);
        var titles = Limited(
            ("content.article.read", new PermissionConstraints { Fields = ["title"] }),
            ("content.article.publish", new PermissionConstraints { Fields = ["title", "slug", "category"] }));
        await Should.NotThrowAsync(() => _database.PublishAsync(titles, id));
    }

    [Fact]
    public async Task AGrantLimitedToSomeFieldsCannotWithdrawTheWholeEntry()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("One")));
        await _database.PublishAsync(Editor, id);
        var before = _database.Trace(id);
        var titles = Limited(("content.article.unpublish", new PermissionConstraints { Fields = ["title"] }));

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.UnpublishAsync(titles, id));
        _database.Trace(id).ShouldBe(before);
    }

    [Fact]
    public async Task DiscardChangesOnlyWhatThePrincipalMayWrite()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["views"] = 1, ["region"] = "eu" }));
        await _database.PublishAsync(Editor, id);
        await _database.PatchAsync(Editor, id, new() { ["title"] = "Two", ["views"] = 2, ["region"] = "us" }, 1);
        var titles = Limited(("content.article.read", null), ("content.article.update", new PermissionConstraints { Fields = ["title"] }));

        var entry = await _database.DiscardAsync(titles, id);

        entry["title"].ShouldBe("One");
        entry["views"].ShouldBe(2L);
        entry["status"].ShouldBe(EntryStatus.Modified);
        _database.Row("cms_article", id)["region"].ShouldBe("us");
        _database.Document(id).Status.ShouldBe(EntryStatus.Modified);
    }

    [Fact]
    public async Task APartialDiscardLeavesHiddenValuesAtEveryDepth()
    {
        var id = IdOf(await _database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().CreateAsync("article", Secretive("One", "published-top", heading: "H1", inner: "published-block"), Token), origin: Origin.Seed));
        await _database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().PublishAsync("article", id, null, Token), origin: Origin.Seed);
        var draft = Secretive("Two", "draft-top", heading: "H2", inner: "draft-block");
        draft["seo"] = new ContentDocument { ["metaTitle"] = "Meta2", ["internal"] = "draft-group" };
        await _database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().UpdateAsync("article", id, draft, 1, Token), origin: Origin.Seed);
        var fields = new PermissionConstraints { Fields = ["sections", "seo"] };
        var limited = Limited(("content.article.read", null), ("content.article.update", fields));

        var entry = await _database.DiscardAsync(limited, id);

        entry["status"].ShouldBe(EntryStatus.Modified);
        var snapshot = System.Text.Json.Nodes.JsonNode.Parse(_database.Versions(id)[^1].Snapshot)!;
        snapshot["sections"]![0]!["heading"]!.GetValue<string>().ShouldBe("H1");
        snapshot["sections"]![0]!["internal"]!.GetValue<string>().ShouldBe("draft-block");
        snapshot["seo"]!["metaTitle"]!.GetValue<string>().ShouldBe("Meta");
        snapshot["seo"]!["internal"]!.GetValue<string>().ShouldBe("draft-group");
        snapshot["secret"]!.GetValue<string>().ShouldBe("draft-top");
        snapshot["title"]!.GetValue<string>().ShouldBe("Two");
    }

    [Fact]
    public async Task DiscardByAGrantLimitedToALocaleLeavesTheSharedFields()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await _database.PatchAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        await _database.PublishAsync(Editor, id);
        await _database.PublishAsync(Editor, id, "fr");
        await _database.PatchAsync(Editor, id, new() { ["views"] = 2, ["title"] = "Salut" }, 1, locale: "fr");
        var french = new PermissionConstraints { Locales = ["fr"] };
        var translator = Limited(("content.article.read", french), ("content.article.update", french));

        var entry = await _database.DiscardAsync(translator, id, "fr", operationLocale: "fr");

        entry["title"].ShouldBe("Bonjour");
        entry["views"].ShouldBe(2L);
        _database.Row("cms_article", id)["views"].ShouldBe(2L);
    }

    [Fact]
    public async Task RestoreIsAWriteOfThePrincipalsAndAnOldVersionIsNoWayPastItsLimits()
    {
        var id = await SecretiveEntryAsync();
        var limited = Limited(("content.article.read", new PermissionConstraints { Fields = ["title", "sections"] }), ("content.article.update", new PermissionConstraints { Fields = ["title", "sections"] }));

        var entry = await _database.RestoreVersionAsync(limited, id, 1);

        entry["title"].ShouldBe("One");
        entry.ContainsKey("views").ShouldBeFalse();
        entry.ContainsKey("secret").ShouldBeFalse();
        Json(entry).ShouldNotContain("hidden");

        // Its own fields came back; views, the hidden field and the hidden value inside the block did not.
        var row = _database.Row("cms_article", id);
        row["views"].ShouldBe(2L);
        row["secret"].ShouldBe("s2");
        var sections = row["sections"].ShouldBeOfType<string>();
        sections.ShouldContain("\"heading\":\"Hi\"");
        sections.ShouldContain("hidden-2");
        sections.ShouldNotContain("hidden-in-block");
    }

    [Fact]
    public async Task AHiddenValueInAGroupStaysOnARestore()
    {
        var system = Principal.System;
        var id = IdOf(await _database.AsAsync(system, services => services.GetRequiredService<IContentService>().CreateAsync("article", new() { ["title"] = "One", ["seo"] = new ContentDocument { ["metaTitle"] = "Old", ["internal"] = "first" } }, Token), origin: Origin.Seed));
        await _database.AsAsync(system, services => services.GetRequiredService<IContentService>().PatchAsync("article", id, new() { ["seo"] = new ContentDocument { ["metaTitle"] = "New", ["internal"] = "second" } }, 1, Token), origin: Origin.Seed);

        var entry = await _database.RestoreVersionAsync(Editor, id, 1);

        ((ContentDocument)entry["seo"]!)["metaTitle"].ShouldBe("Old");
        ((ContentDocument)entry["seo"]!).ContainsKey("internal").ShouldBeFalse();
        var snapshot = _database.Versions(id)[^1].Snapshot;
        snapshot.ShouldContain("second");
        snapshot.ShouldNotContain("first");
    }

    [Fact]
    public async Task ARestoreByAGrantLimitedToALocaleLeavesTheSharedFields()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await _database.PatchAsync(Editor, id, new() { ["title"] = "Un" }, 0, locale: "fr");
        await _database.PatchAsync(Editor, id, new() { ["title"] = "Deux", ["views"] = 2 }, 1, locale: "fr");
        var french = new PermissionConstraints { Locales = ["fr"] };
        var translator = Limited(("content.article.read", french), ("content.article.update", french));

        var entry = await _database.RestoreVersionAsync(translator, id, 1, "fr");

        entry.ShouldNotBeNull();
        _database.Row("cms_article", id)["views"].ShouldBe(2L);
        _database.Row("cms_article_i18n", id, "fr")["title"].ShouldBe("Un");
        _database.Row("cms_article_i18n", id, "en")["title"].ShouldBe("Hello");
    }

    [Fact]
    public async Task ARestoreOfASharedFieldMovesThePublishedOtherLocaleToModified()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await _database.PatchAsync(Editor, id, new() { ["title"] = "Un" }, 0, locale: "fr");
        await _database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 1);
        await _database.PublishAsync(Editor, id);
        await _database.PublishAsync(Editor, id, "fr");

        await _database.RestoreVersionAsync(Editor, id, 1);

        _database.Document(id).Status.ShouldBe(EntryStatus.Modified);
        _database.Document(id, "fr").Status.ShouldBe(EntryStatus.Modified);
    }

    [Fact]
    public async Task TheSystemPrincipalRestoresTheHiddenValuesToo()
    {
        var id = await SecretiveEntryAsync();

        await _database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().RestoreVersionAsync("article", id, 1, null, Token), origin: Origin.Seed);

        var row = _database.Row("cms_article", id);
        row["secret"].ShouldBe("s1");
        row["sections"].ShouldBeOfType<string>().ShouldContain("hidden-in-block");
    }

    [Fact]
    public async Task ReadOnlyValuesStayOnARestore()
    {
        var id = IdOf(await _database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().CreateAsync("article", new() { ["title"] = "One", ["stamp"] = "first" }, Token), origin: Origin.Seed));
        await _database.AsAsync(Principal.System, services => services.GetRequiredService<IContentService>().PatchAsync("article", id, new() { ["stamp"] = "second" }, 1, Token), origin: Origin.Seed);

        await _database.RestoreVersionAsync(Editor, id, 1);

        _database.Row("cms_article", id)["stamp"].ShouldBe("second");
    }

    [Fact]
    public async Task AWriterWhoCannotReadMayNotRestoreOrDiscard()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("One")));
        await _database.PublishAsync(Editor, id);
        await _database.PatchAsync(Editor, id, Titled("Two"), 1);
        var before = _database.Trace(id);
        var writer = Limited(("content.article.update", null));

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.RestoreVersionAsync(writer, id, 1));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.DiscardAsync(writer, id));
        await Should.ThrowAsync<ValidationException>(() => _database.VersionAsync(writer, id, 1));
        _database.Trace(id).ShouldBe(before);
    }

    [Fact]
    public async Task ADiscardOrRestoreCannotCheckAGuessAtAFieldItCannotRead()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["code"] = "SECRET" }));
        await _database.PublishAsync(Editor, id);
        var guesser = Limited(
            ("content.article.read", new PermissionConstraints { Fields = ["title"] }),
            ("content.article.update", new PermissionConstraints { Fields = ["title", "code"] }));
        List<string> answers = [];

        var version = 1;
        foreach (var guess in new[] { "WRONG", "SECRET" })
        {
            version = VersionOf(await _database.PatchAsync(guesser, id, new() { ["code"] = guess }, version));
            var before = _database.Trace(id);
            foreach (var call in new Func<Task<ContentDocument>>[] { () => _database.DiscardAsync(guesser, id), () => _database.RestoreVersionAsync(guesser, id, 1) })
            {
                var refused = await Should.ThrowAsync<ContentAccessDeniedException>(call);
                answers.Add(refused.Message);
            }

            _database.Trace(id).ShouldBe(before);
        }

        answers.Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ARestoreKeepsARelationTargetItMayNotReadAndDoesNotBringOneBack()
    {
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var other = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Bob" }, "author"));
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["author"] = author }));
        await _database.PatchAsync(Editor, id, new() { ["author"] = other, ["title"] = "Two" }, 1);
        var limited = Limited(("content.article.read", null), ("content.article.update", null));

        await _database.RestoreVersionAsync(limited, id, 1);

        _database.References(id, Core.Storage.ContentState.Draft).ShouldHaveSingleItem().TargetId.ShouldBe(other);
        _database.Row("cms_article_i18n", id, "en")["title"].ShouldBe("One");
    }

    [Fact]
    public async Task ThePublishErrorOfAFieldItMayNotReadNamesNeitherTheFieldNorTheValue()
    {
        var schema = SampleSchema.NewBuilder()
            .AddCollection("page", type => type
                .Field("name", "text", field => field.Required())
                .Field("sensitive", "text", field => field.Required())
                .Field("internal", "text", field => field.Hidden().Required()))
            .Build();
        using var database = new ServiceDatabase(schema: schema);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "One" }, "page"));
        var blind = WithRole(database.Database.AddRoleForTest(
            "Blind",
            ("content.page.publish", null),
            ("content.page.read", new PermissionConstraints { Fields = ["name"] })));

        var error = (await Should.ThrowAsync<ValidationException>(() => database.PublishAsync(blind, id, type: "page"))).Errors.ShouldHaveSingleItem();
        error.Path.ShouldBe("");
        error.Message.ShouldNotContain("sensitive");
        error.Message.ShouldNotContain("internal");

        var errors = (await Should.ThrowAsync<ValidationException>(() => database.PublishAsync(Editor, id, type: "page"))).Errors;
        errors.Select(item => item.Path).ShouldBe(["sensitive", ""], ignoreOrder: true);
    }

    private sealed class ThrowAt(HookKind kind) : IBeforePublishHook<ContentDocument>, IAfterPublishHook<ContentDocument>, IBeforeUnpublishHook<ContentDocument>, IAfterUnpublishHook<ContentDocument>, IAfterUpdateHook<ContentDocument>
    {
        public HookKind Kind { get; set; } = kind;

        public bool Armed { get; set; }

        private ValueTask At(HookKind kind) =>
            Armed && kind == Kind ? throw new InvalidOperationException("hook failed") : ValueTask.CompletedTask;

        public ValueTask BeforePublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (Armed && Kind == HookKind.BeforePublish)
            {
                context.Cancel("no", "Refused.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask AfterPublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => At(HookKind.AfterPublish);

        public ValueTask BeforeUnpublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (Armed && Kind == HookKind.BeforeUnpublish)
            {
                context.Cancel("no", "Refused.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask AfterUnpublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => At(HookKind.AfterUnpublish);

        public ValueTask AfterUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => At(HookKind.AfterUpdate);
    }

    private sealed class Sink : IEntryChangeSink
    {
        public List<EntryChange> Changes { get; } = [];

        public ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    [Theory]
    [InlineData(HookKind.BeforePublish)]
    [InlineData(HookKind.AfterPublish)]
    [InlineData(HookKind.BeforeUnpublish)]
    [InlineData(HookKind.AfterUnpublish)]
    [InlineData(HookKind.AfterUpdate)]
    public async Task AHookThatThrowsLeavesNothingBehindAndTellsNoOne(HookKind kind)
    {
        var hook = new ThrowAt(kind);
        var sink = new Sink();
        using var database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton<IBeforePublishHook<ContentDocument>>(hook);
            cms.Services.AddSingleton<IAfterPublishHook<ContentDocument>>(hook);
            cms.Services.AddSingleton<IBeforeUnpublishHook<ContentDocument>>(hook);
            cms.Services.AddSingleton<IAfterUnpublishHook<ContentDocument>>(hook);
            cms.Services.AddSingleton<IAfterUpdateHook<ContentDocument>>(hook);
            cms.Services.AddSingleton<IEntryChangeSink>(sink);
        });

        var author = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "One", ["author"] = author }));
        await database.PublishAsync(Editor, id);
        await database.PatchAsync(Editor, id, new() { ["title"] = "Two", ["author"] = null, ["views"] = 2 }, 1);

        var before = database.Trace(id);
        sink.Changes.Clear();
        hook.Armed = true;

        Func<Task>[] calls = kind switch
        {
            HookKind.BeforePublish or HookKind.AfterPublish => [() => database.PublishAsync(Editor, id)],
            HookKind.BeforeUnpublish or HookKind.AfterUnpublish => [() => database.UnpublishAsync(Editor, id)],
            _ => [() => database.DiscardAsync(Editor, id), () => database.RestoreVersionAsync(Editor, id, 1)],
        };

        foreach (var call in calls)
        {
            await Should.ThrowAsync<Exception>(call);
            database.Trace(id).ShouldBe(before);
        }

        sink.Changes.ShouldBeEmpty();

        hook.Armed = false;
        await calls[0]();
        database.Trace(id).ShouldNotBe(before);
    }

    private sealed class SetRegion : IBeforePublishHook<ContentDocument>
    {
        public ValueTask BeforePublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry["region"] = "from-hook";
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task APublishHookIsHeldToTheLimitsOfThePrincipal()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforePublishHook<ContentDocument>, SetRegion>());
        var id = IdOf(await database.CreateAsync(Editor, Titled("One")));
        var titles = WithRole(database.Database.AddRoleForTest("Titles", ("content.article.publish", new PermissionConstraints { Fields = ["title", "slug", "category"] })));
        var before = database.Trace(id);

        var error = (await Should.ThrowAsync<ValidationException>(() => database.PublishAsync(titles, id))).Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(AuthorizationErrorCodes.FieldNotWritable);
        database.Trace(id).ShouldBe(before);
    }

    [Fact]
    public async Task AnAuditColumnNamesTheActorOfAJobAndAuthorizationGoesByThePrincipal()
    {
        var scheduler = new Principal(PrincipalKind.AdminUser, Guid.CreateVersion7(), "Scheduler");
        var id = IdOf(await _database.CreateAsync(Editor, Titled("One")));

        await InJob(scheduler, service => service.PublishAsync("article", id, null, Token));
        _database.Document(id).UpdatedBy.ShouldBe(scheduler.Id);

        await _database.PatchAsync(Editor, id, Titled("Two"), 1);
        await InJob(scheduler, service => service.RestoreVersionAsync("article", id, 1, null, Token));
        _database.Document(id).UpdatedBy.ShouldBe(scheduler.Id);
        _database.Versions(id)[^1].AuthorId.ShouldBe(scheduler.Id);

        await _database.PatchAsync(Editor, id, Titled("Three"), 3);
        await InJob(scheduler, service => service.DiscardDraftAsync("article", id, null, Token));
        _database.Versions(id)[^1].AuthorId.ShouldBe(scheduler.Id);

        await InJob(scheduler, service => service.UnpublishAsync("article", id, null, Token));
        _database.Document(id).UpdatedBy.ShouldBe(scheduler.Id);

        // An actor with no grants takes nothing from the job, and a client's actor gives it nothing.
        var nobody = Limited();
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.AsAsync(nobody, services =>
        {
            var accessor = services.GetRequiredService<IOperationContextAccessor>();
            accessor.Current = accessor.Current! with { Actor = Editor };
            return services.GetRequiredService<IContentService>().PublishAsync("article", id, null, Token);
        }));
    }

    private async Task InJob(Principal actor, Func<IContentService, Task> work)
    {
        using var scope = _database.Database.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = new OperationContext(Principal.System, Origin.Job, SiteA, "en", null, "job", DateTimeOffset.UtcNow) { Actor = actor };
        try
        {
            await work(scope.ServiceProvider.GetRequiredService<IContentService>());
        }
        finally
        {
            accessor.Current = null;
        }
    }

    [Fact]
    public async Task AnEntryOfAnotherSiteOrTypeTakesNoTransitionAndAGlobalOneNeedsAnInstanceWideGrant()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("One")));
        var before = _database.Trace(id);
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().PublishAsync("article", id, null, Token), site: SiteB));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().GetVersionsAsync("article", id, null, Token), site: SiteB));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PublishAsync(Editor, id, type: "author"));
        _database.Trace(id).ShouldBe(before);

        var tag = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "net" }, "tag"));
        var inA = WithRole(_database.Database.AddRoleForTest("Site A", SiteA.Value, ("content.tag.publish", null), ("content.tag.unpublish", null), ("content.tag.update", null)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PublishAsync(inA, tag, type: "tag"));
    }

    [Fact]
    public async Task APublishThatLostTheSwapChangesNothing()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("One")));
        await _database.PatchAsync(Editor, id, Titled("Two"), 1);
        var before = _database.Trace(id);

        var stale = _database.Document(id);
        stale.DraftVersion = 1;
        await _database.AsAsync(Editor, async services =>
        {
            (await services.GetRequiredService<EntryStore>().MarkPublishedAsync(stale, DateTimeOffset.UtcNow, Editor.Id, Token)).ShouldBeFalse();
            return true;
        });

        _database.Trace(id).ShouldBe(before);
    }

    [Fact]
    public async Task AStaleExpectedVersionTellsNothingThePrincipalMayNotRead()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["views"] = 1 }));
        await _database.PatchAsync(Editor, id, Titled("Two"), 1);
        var limited = Limited(("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.article.publish", null));

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => _database.PublishAsync(limited, id, expected: 1));

        conflict.Current!.ContainsKey("views").ShouldBeFalse();
    }
}
