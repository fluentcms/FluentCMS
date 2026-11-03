using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Events;
using FluentCMS.Events;
using FluentCMS.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class TrashTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private Guid Purger() => _database.Database.AddRoleForTest(
        "Purger",
        ("content.*.create", null),
        ("content.*.read", null),
        ("content.*.update", null),
        ("content.*.delete", null),
        ("content.*.restore", null),
        ("content.*.publish", null),
        ("content.*.purge", null));

    private async Task<Guid> TrashedAsync(string title, Principal? by = null, string? code = null)
    {
        var entry = new ContentDocument { ["title"] = title };
        if (code is not null)
        {
            entry["code"] = code;
        }

        var id = IdOf(await _database.CreateAsync(by ?? Editor, entry));
        await _database.DeleteAsync(by ?? Editor, id);
        return id;
    }

    [Fact]
    public async Task ATrashedEntryLeavesEveryReadAndComesBackWithItsVersions()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("Hello")));
        await _database.PatchAsync(Editor, id, Titled("Two"), 1);
        await _database.PublishAsync(Editor, id);
        await _database.PatchAsync(Editor, id, Titled("Three"), 2);
        var versions = _database.Versions(id).Select(version => (version.Locale, version.Number, version.StatusAfter, version.Snapshot)).ToList();

        await _database.DeleteAsync(Editor, id);

        (await _database.GetAsync(Editor, id)).ShouldBeNull();
        (await _database.GetAsync(Editor, id, new GetOptions())).ShouldBeNull();
        (await _database.QueryAsync(Editor)).Items.ShouldBeEmpty();
        (await _database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Published })).Items.ShouldBeEmpty();
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.VersionsAsync(Editor, id));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.VersionAsync(Editor, id, 1));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PublishAsync(Editor, id));

        var restored = await _database.RestoreAsync(Editor, id);

        restored["title"].ShouldBe("Three");
        restored["version"].ShouldBe(3);
        _database.Versions(id).Select(version => (version.Locale, version.Number, version.StatusAfter, version.Snapshot)).ShouldBe(versions);
        (await _database.VersionsAsync(Editor, id)).Count.ShouldBe(3);
        (await _database.GetAsync(Editor, id, new GetOptions()))!["title"].ShouldBe("Two");
    }

    [Fact]
    public async Task TheTrashListsWhatWasDeletedWithWhenAndByWhom()
    {
        var kept = IdOf(await _database.CreateAsync(Editor, Titled("Kept")));
        var one = await TrashedAsync("One");
        var two = await TrashedAsync("Two");

        var page = await _database.TrashAsync(Editor);

        page.Items.Select(entry => (Guid)entry["id"]!).ShouldBe([one, two], ignoreOrder: true);
        page.Items.Select(entry => (Guid)entry["id"]!).ShouldNotContain(kept);
        foreach (var entry in page.Items)
        {
            entry["deletedAt"].ShouldNotBeNull();
            entry["deletedBy"].ShouldBe(Editor.Id);
        }

        page.Items.Single(entry => (Guid)entry["id"]! == one)["title"].ShouldBe("One");

        var first = await _database.TrashAsync(Editor, new ContentQuery { Page = new PageRequest { Size = 1 } });
        first.Items.Count.ShouldBe(1);
        first.NextCursor.ShouldNotBeNull();
        var second = await _database.TrashAsync(Editor, new ContentQuery { Cursor = first.NextCursor, Page = new PageRequest { Size = 1 } });
        second.Items.Count.ShouldBe(1);
        second.Items[0]["id"].ShouldNotBe(first.Items[0]["id"]);

        await _database.RestoreAsync(Editor, one);
        (await _database.TrashAsync(Editor)).Items.ShouldHaveSingleItem()["id"].ShouldBe(two);
    }

    [Fact]
    public async Task OnlyWhoCanRestoreOrPurgeAndReadMayLookAtTheTrash()
    {
        await TrashedAsync("Hidden");
        var restoreOnly = _database.Database.AddRoleForTest("RestoreOnly", ("content.article.read", null), ("content.article.restore", null));
        var purgeOnly = _database.Database.AddRoleForTest("PurgeOnly", ("content.article.read", null), ("content.article.purge", null));
        var readOnly = _database.Database.AddRoleForTest("ReadOnly", ("content.article.read", null));
        var blind = _database.Database.AddRoleForTest("Blind", ("content.article.restore", null), ("content.article.purge", null));

        (await _database.TrashAsync(WithRole(restoreOnly))).Items.ShouldHaveSingleItem();
        (await _database.TrashAsync(WithRole(purgeOnly))).Items.ShouldHaveSingleItem();
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.TrashAsync(WithRole(readOnly)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.TrashAsync(AuthorRole()));

        // Without the read the type is not there for it, which is what a type that does not exist says.
        var unknown = await Should.ThrowAsync<ValidationException>(() => _database.TrashAsync(Editor, type: "nothing"));
        var unreadable = await Should.ThrowAsync<ValidationException>(() => _database.TrashAsync(WithRole(blind)));
        unreadable.Errors.Select(error => (error.Path, error.Code)).ShouldBe(unknown.Errors.Select(error => (error.Path, error.Code)));
        await Should.ThrowAsync<ValidationException>(() => _database.TrashAsync(Principal.Anonymous));
    }

    [Fact]
    public async Task TheTrashListHoldsTheReadRowRuleAndTheFieldLimits()
    {
        var role = _database.Database.AddRoleForTest(
            "Tidy",
            ("content.*.create", null),
            ("content.*.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule, Fields = ["title"] }),
            ("content.*.delete", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }),
            ("content.*.restore", null));
        var ann = WithRole(role, Guid.CreateVersion7());
        var bob = WithRole(role, Guid.CreateVersion7());
        var mine = IdOf(await _database.CreateAsync(ann, new() { ["title"] = "Mine", ["region"] = "eu" }));
        var theirs = IdOf(await _database.CreateAsync(bob, Titled("Theirs")));
        await _database.DeleteAsync(ann, mine);
        await _database.DeleteAsync(bob, theirs);

        var listed = (await _database.TrashAsync(ann)).Items.ShouldHaveSingleItem();

        listed["id"].ShouldBe(mine);
        listed["title"].ShouldBe("Mine");
        listed.ContainsKey("region").ShouldBeFalse();
        (await _database.TrashAsync(Editor)).Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task TheTrashOfAnotherSiteOrLocaleIsNotListed()
    {
        var inA = IdOf(await _database.CreateAsync(Editor, Titled("In A")));
        var inB = IdOf(await _database.CreateAsync(Editor, Titled("In B"), site: SiteB));
        await _database.DeleteAsync(Editor, inA);
        await _database.DeleteInAsync(Editor, inB, SiteB);
        var french = _database.Database.AddRoleForTest("French", ("content.article.read", null), ("content.article.restore", new PermissionConstraints { Locales = ["fr"] }));

        (await _database.TrashAsync(Editor)).Items.ShouldHaveSingleItem()["id"].ShouldBe(inA);
        (await _database.TrashAsync(Editor, site: SiteB)).Items.ShouldHaveSingleItem()["id"].ShouldBe(inB);
        await Should.ThrowAsync<ValidationException>(() => _database.TrashAsync(Editor, new ContentQuery { Locale = "all" }));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.TrashAsync(WithRole(french), locale: "en"));
        (await _database.TrashAsync(WithRole(french), locale: "fr")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ATargetInTheTrashNeverMatchesAFilterOrFillsAPopulate()
    {
        var ann = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, type: "author"));
        var inTrash = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Written", ["author"] = ann }));
        await _database.DeleteAsync(Editor, inTrash);
        var byAnn = new ContentQuery { Filter = new FieldFilter("author.name", FilterOperator.Equal, "Ann"), Populate = ["author"] };

        var withLiveAuthor = (await _database.TrashAsync(Editor, byAnn)).Items.ShouldHaveSingleItem();
        withLiveAuthor["id"].ShouldBe(inTrash);
        ((ContentDocument)withLiveAuthor["author"]!)["name"].ShouldBe("Ann");

        await _database.DeleteAsync(Editor, ann, type: "author");

        (await _database.TrashAsync(Editor, byAnn)).Items.ShouldBeEmpty();
        var unpopulated = (await _database.TrashAsync(Editor, new ContentQuery { Populate = ["author"] })).Items.ShouldHaveSingleItem();
        ((ContentDocument)unpopulated["author"]!).ContainsKey("name").ShouldBeFalse();
        (await _database.TrashAsync(Editor, type: "author")).Items.ShouldHaveSingleItem()["id"].ShouldBe(ann);
    }

    private async Task<int> MatchesAsync(Guid target, string field, QueryStatus status, bool trash = false)
    {
        var query = new ContentQuery { Filter = new FieldFilter(field, FilterOperator.Equal, target), Status = status };
        return (trash ? await _database.TrashAsync(Editor, query) : await _database.QueryAsync(Editor, query)).Items.Count;
    }

    [Fact]
    public async Task AFilterOnTheIdOfARelationMatchesOnlyATargetTheSameReadWouldShow()
    {
        var tag = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Net" }, type: "tag"));
        var ann = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, type: "author"));
        var entry = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Tagged", ["tags"] = new[] { tag }, ["author"] = ann }));
        var draft = QueryStatus.Draft;
        var published = QueryStatus.Published;

        // A target that exists but is not published is not shown by a published read.
        (await MatchesAsync(tag, "tags", draft)).ShouldBe(1);
        (await MatchesAsync(ann, "author", draft)).ShouldBe(1);
        await _database.PublishAsync(Editor, entry);
        (await MatchesAsync(tag, "tags", published)).ShouldBe(0);
        (await MatchesAsync(ann, "author", published)).ShouldBe(0);

        await _database.PublishAsync(Editor, tag, type: "tag");
        await _database.PublishAsync(Editor, ann, type: "author");
        (await MatchesAsync(tag, "tags", published)).ShouldBe(1);
        (await MatchesAsync(ann, "author", published)).ShouldBe(1);

        // A target in the trash is not shown by any read, whether its type is global or belongs to a site.
        await _database.DeleteAsync(Editor, tag, type: "tag");
        await _database.DeleteAsync(Editor, ann, type: "author");
        (await MatchesAsync(tag, "tags", draft)).ShouldBe(0);
        (await MatchesAsync(ann, "author", draft)).ShouldBe(0);
        (await MatchesAsync(tag, "tags", published)).ShouldBe(0);
        (await MatchesAsync(ann, "author", published)).ShouldBe(0);
        var anyOf = new ContentQuery { Filter = new FieldFilter("tags", FilterOperator.In, new[] { tag }), Status = draft };
        (await _database.QueryAsync(Editor, anyOf)).Items.ShouldBeEmpty();

        // The same in the trash list.
        await _database.RestoreAsync(Editor, tag, type: "tag");
        await _database.RestoreAsync(Editor, ann, type: "author");
        await _database.DeleteAsync(Editor, entry);
        (await MatchesAsync(tag, "tags", draft, trash: true)).ShouldBe(1);
        (await MatchesAsync(ann, "author", draft, trash: true)).ShouldBe(1);
        await _database.DeleteAsync(Editor, tag, type: "tag");
        await _database.DeleteAsync(Editor, ann, type: "author");
        (await MatchesAsync(tag, "tags", draft, trash: true)).ShouldBe(0);
        (await MatchesAsync(ann, "author", draft, trash: true)).ShouldBe(0);
    }

    [Fact]
    public async Task AFilterOnTheIdOfARelationDoesNotMatchAnEntryOfAnotherType()
    {
        var ann = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, type: "author"));
        var other = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Other", ["author"] = ann }));

        (await MatchesAsync(other, "tags", QueryStatus.Draft)).ShouldBe(0);
        (await MatchesAsync(ann, "author", QueryStatus.Draft)).ShouldBe(1);
    }

    [Fact]
    public async Task PurgeDeletesEveryRowOfAnEntryInEveryLocaleAndEveryVersion()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["code"] = "one" }));
        await _database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        await _database.PublishAsync(Editor, id);
        await _database.PublishAsync(Editor, id, "fr");
        await _database.PatchAsync(Editor, id, Titled("Hello again"), 1);
        var ann = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, type: "author"));
        await _database.PatchAsync(Editor, id, new() { ["author"] = ann }, 2);
        var other = IdOf(await _database.CreateAsync(Editor, Titled("Other")));
        _database.Held(id).ShouldNotBe(TrashDatabase.Nothing);
        _database.Versions(id).ShouldContain(version => version.StatusAfter == EntryStatus.Published);
        await _database.DeleteAsync(Editor, id);
        var otherHeld = _database.Held(other);

        await _database.PurgeAsync(WithRole(Purger()), id);

        _database.Held(id).ShouldBe(TrashDatabase.Nothing);
        _database.Held(other).ShouldBe(otherHeld);
        (await _database.TrashAsync(Editor)).Items.ShouldBeEmpty();
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.RestoreAsync(Editor, id));
        (await _database.GetAsync(Editor, ann, type: "author"))!["name"].ShouldBe("Ann");
    }

    [Fact]
    public async Task PurgeNeedsItsOwnPermission()
    {
        var id = await TrashedAsync("Hello");
        var restorer = _database.Database.AddRoleForTest("Restorer", ("content.*.restore", null), ("content.*.read", null));

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PurgeAsync(Editor, id));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PurgeAsync(AuthorRole(), id));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PurgeAsync(Principal.Anonymous, id));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PurgeAsync(WithRole(restorer), id));
        _database.Held(id).ShouldNotBe(TrashDatabase.Nothing);

        await _database.PurgeAsync(WithRole(BuiltInRoles.SuperAdmin), id);

        _database.Held(id).ShouldBe(TrashDatabase.Nothing);
    }

    [Fact]
    public async Task OnlyAnEntryInTheTrashCanBePurged()
    {
        var live = IdOf(await _database.CreateAsync(Editor, Titled("Alive")));
        var author = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, type: "author"));
        await _database.DeleteAsync(Editor, author, type: "author");
        var admin = WithRole(BuiltInRoles.SuperAdmin);

        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PurgeAsync(admin, live));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PurgeAsync(admin, Guid.CreateVersion7()));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PurgeAsync(admin, author));

        _database.Held(live).ShouldNotBe(TrashDatabase.Nothing);
        (await _database.TrashAsync(Editor, type: "author")).Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AnEntryThePurgeRuleExcludesIsNotFoundWhateverTheIdIsAndStaysInTheTrash()
    {
        var role = _database.Database.AddRoleForTest(
            "OwnPurger",
            ("content.*.create", null),
            ("content.*.delete", null),
            ("content.*.purge", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }));
        var ann = WithRole(role, Guid.CreateVersion7());
        var bob = WithRole(role, Guid.CreateVersion7());
        var mine = IdOf(await _database.CreateAsync(ann, Titled("Mine")));
        var theirs = IdOf(await _database.CreateAsync(bob, Titled("Theirs")));
        await _database.DeleteAsync(ann, mine);
        await _database.DeleteAsync(bob, theirs);
        var missing = Guid.CreateVersion7();

        var refused = await Should.ThrowAsync<EntryNotFoundException>(() => _database.PurgeAsync(ann, theirs));
        var absent = await Should.ThrowAsync<EntryNotFoundException>(() => _database.PurgeAsync(ann, missing));
        refused.Message.Replace(theirs.ToString(), "{id}", StringComparison.Ordinal).ShouldBe(absent.Message.Replace(missing.ToString(), "{id}", StringComparison.Ordinal));
        _database.Held(theirs).ShouldNotBe(TrashDatabase.Nothing);

        await _database.PurgeAsync(ann, mine);

        _database.Held(mine).ShouldBe(TrashDatabase.Nothing);
        (await _database.TrashAsync(Editor)).Items.ShouldHaveSingleItem()["id"].ShouldBe(theirs);
    }

    [Fact]
    public async Task PurgeNeedsTheGrantOnEveryLocaleOfTheEntryAndTheSiteOfIt()
    {
        var both = IdOf(await _database.CreateAsync(Editor, Titled("Hello")));
        await _database.UpdateAsync(Editor, both, Titled("Bonjour"), 0, locale: "fr");
        var englishOnly = IdOf(await _database.CreateAsync(Editor, Titled("Only English")));
        var elsewhere = IdOf(await _database.CreateAsync(Editor, Titled("In B"), site: SiteB));
        await _database.DeleteAsync(Editor, both);
        await _database.DeleteAsync(Editor, englishOnly);
        await _database.DeleteInAsync(Editor, elsewhere, SiteB);
        var french = WithRole(_database.Database.AddRoleForTest("FrenchPurger", ("content.article.purge", new PermissionConstraints { Locales = ["fr"] })));
        var admin = WithRole(BuiltInRoles.SuperAdmin);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PurgeAsync(french, both, locale: "fr"));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PurgeAsync(french, englishOnly, locale: "fr"));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PurgeAsync(french, both, locale: "en"));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PurgeAsync(admin, elsewhere));

        new[] { both, englishOnly, elsewhere }.ShouldAllBe(id => _database.Held(id) != TrashDatabase.Nothing);

        await _database.PurgeAsync(admin, elsewhere, site: SiteB);
        _database.Held(elsewhere).ShouldBe(TrashDatabase.Nothing);
    }

    [Fact]
    public async Task APurgeInATransactionThatRollsBackLeavesTheEntryInTheTrash()
    {
        var id = IdOf(await _database.CreateAsync(Editor, Titled("Hello")));
        await _database.PublishAsync(Editor, id);
        await _database.DeleteAsync(Editor, id);
        var before = _database.Trace(id);

        await _database.AsAsync(WithRole(BuiltInRoles.SuperAdmin), async services =>
        {
            await using var transaction = await services.GetRequiredService<FluentCMS.Core.Storage.CmsDbContext>().Database.BeginTransactionAsync(Token);
            await services.GetRequiredService<IContentService>().PurgeAsync("article", id, Token);
            await transaction.RollbackAsync(Token);
            return true;
        });

        _database.Trace(id).ShouldBe(before);
        (await _database.RestoreAsync(Editor, id))["title"].ShouldBe("Hello");
    }

    [Fact]
    public async Task OnlyAPurgeFreesTheValuesTheTrashHolds()
    {
        var id = await TrashedAsync("First", code: "taken");
        var again = await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["title"] = "Second", ["code"] = "taken" }));
        again.Errors.ShouldNotBeEmpty();

        await _database.PurgeAsync(WithRole(Purger()), id);

        (await _database.CreateAsync(Editor, new() { ["title"] = "Second", ["code"] = "taken" }))["code"].ShouldBe("taken");
    }

    [Fact]
    public async Task APurgedTargetIsAsMissingAsATrashedOneWasForPublishing()
    {
        var ann = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Ann" }, type: "author"));
        await _database.PublishAsync(Editor, ann, type: "author");
        var source = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Written", ["author"] = ann }));
        (await _database.PublishAsync(Editor, source)).Warnings.ShouldBeEmpty();

        await _database.DeleteAsync(Editor, ann, type: "author");
        await _database.PatchAsync(Editor, source, Titled("Trashed target"), 1);
        var trashed = (await _database.PublishAsync(Editor, source)).Warnings.ShouldHaveSingleItem();
        (await _database.GetAsync(Editor, source, new GetOptions()))!["author"].ShouldBeNull();

        await _database.PurgeAsync(WithRole(BuiltInRoles.SuperAdmin), ann, type: "author");
        await _database.PatchAsync(Editor, source, Titled("Purged target"), 2);
        var purged = (await _database.PublishAsync(Editor, source)).Warnings.ShouldHaveSingleItem();

        purged.Code.ShouldBe(PublishWarningCodes.UnavailableReference);
        purged.Code.ShouldBe(trashed.Code);
        (await _database.GetAsync(Editor, source, new GetOptions()))!["author"].ShouldBeNull();
    }

    private sealed class Heard(Seen seen) : IEventHandler<EntryPurgedEvent>
    {
        public ValueTask HandleAsync(EntryPurgedEvent message, CancellationToken cancellationToken) => seen.HeardAsync("purged", message, null, cancellationToken);
    }

    [Fact]
    public async Task APurgeRaisesOneEventPerLocaleOnlyWhenItCommits()
    {
        var seen = new Seen();
        using var database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton(seen);
            cms.Events.AddHandler<EntryPurgedEvent, Heard>();
        });
        var id = IdOf(await database.CreateAsync(Editor, Titled("Hello")));
        await database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        await database.PatchAsync(Editor, id, Titled("Hello again"), 1);
        await database.DeleteAsync(Editor, id);
        var admin = WithRole(BuiltInRoles.SuperAdmin);

        await Should.ThrowAsync<ContentAccessDeniedException>(() => database.PurgeAsync(Editor, id));
        seen.Of<EntryPurgedEvent>().ShouldBeEmpty();

        await database.PurgeAsync(admin, id);

        var heard = seen.Of<EntryPurgedEvent>().OrderBy(@event => @event.Locale, StringComparer.Ordinal).ToList();
        heard.Select(@event => (@event.Type, @event.EntryId, @event.Locale, @event.Version)).ShouldBe([("article", id, "en", 2), ("article", id, "fr", 1)]);
    }
}
