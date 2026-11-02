using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Query;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class ContentServiceAccessTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private sealed class SetRegion : IBeforeUpdateHook<ContentDocument>
    {
        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry["region"] = "from-hook";
            context.Entry["secret"] = "from-hook";
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AFieldThePrincipalCannotReadNeverAppearsInGetQueryOrAHooksReturnedEntry()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeUpdateHook<ContentDocument>, SetRegion>());
        var entry = await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 5, ["region"] = "eu" });
        var role = database.Database.AddRoleForTest("Limited", ("content.article.read", new PermissionConstraints { Fields = ["title"] }), ("content.article.update", null));
        var limited = WithRole(role);

        var read = (await database.GetAsync(limited, IdOf(entry)))!;
        read["title"].ShouldBe("Hello");
        read.ContainsKey("views").ShouldBeFalse();
        read.ContainsKey("region").ShouldBeFalse();

        var listed = (await database.QueryAsync(limited)).Items.ShouldHaveSingleItem();
        listed.ContainsKey("views").ShouldBeFalse();

        var returned = await database.PatchAsync(limited, IdOf(entry), Titled("Changed"), 1);
        returned["title"].ShouldBe("Changed");
        returned.ContainsKey("region").ShouldBeFalse();
        returned.ContainsKey("secret").ShouldBeFalse();
        returned.ContainsKey("views").ShouldBeFalse();

        database.Row("cms_article", IdOf(entry))["region"].ShouldBe("from-hook");
    }

    [Fact]
    public async Task ANotReadableEntryStillGivesItsWriterTheIdAndVersion()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Hello"));
        var role = _database.Database.AddRoleForTest("WriteOnly", ("content.article.update", null), ("content.article.create", null));

        var updated = await _database.PatchAsync(WithRole(role), IdOf(entry), Titled("Changed"), 1);
        updated.Keys.ShouldBe(["id", "version"]);
        updated["version"].ShouldBe(2);

        var created = await _database.CreateAsync(WithRole(role), Titled("New"));
        created.Keys.ShouldBe(["id", "version"]);

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => _database.PatchAsync(WithRole(role), IdOf(entry), Titled("x"), 1));
        conflict.CurrentVersion.ShouldBe(2);
        conflict.Current.ShouldBeNull();
    }

    [Fact]
    public async Task AuthorReadsAndEditsOnlyTheirOwnEntries()
    {
        var ann = AuthorRole(Guid.CreateVersion7());
        var bob = AuthorRole(Guid.CreateVersion7());

        var mine = await _database.CreateAsync(ann, Titled("Mine"));
        var theirs = await _database.CreateAsync(bob, Titled("Theirs"));
        var id = IdOf(theirs);

        (await _database.GetAsync(ann, IdOf(mine))).ShouldNotBeNull();
        (await _database.QueryAsync(ann)).Items.ShouldHaveSingleItem()["title"].ShouldBe("Mine");
        await Should.NotThrowAsync(() => _database.PatchAsync(ann, IdOf(mine), Titled("Changed"), 1));

        (await _database.GetAsync(ann, id)).ShouldBeNull();
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PatchAsync(ann, id, Titled("Stolen"), 1));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.UpdateAsync(ann, id, Titled("Stolen"), 1));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.DeleteAsync(ann, id));

        (await _database.GetAsync(bob, id))!["title"].ShouldBe("Theirs");
    }

    [Fact]
    public async Task AnEntryThatIsNotThereAndOneThatIsRefusedLookTheSame()
    {
        var theirs = await _database.CreateAsync(AuthorRole(Guid.CreateVersion7()), Titled("Theirs"));
        var ann = AuthorRole(Guid.CreateVersion7());

        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PatchAsync(ann, Guid.CreateVersion7(), Titled("x"), 1));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PatchAsync(ann, IdOf(theirs), Titled("x"), 1));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PatchAsync(Editor, Guid.CreateVersion7(), Titled("x"), 1));
    }

    [Fact]
    public async Task AnEntryOfAnotherSiteOrTypeCannotBeReached()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Hello"));
        var id = IdOf(entry);

        (await _database.GetAsync(Editor, id, site: SiteB)).ShouldBeNull();
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.UpdateAsync(Editor, id, Titled("x"), 1, site: SiteB));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.UpdateAsync(Editor, id, new() { ["name"] = "x" }, 1, "author"));
    }

    [Fact]
    public async Task ARoleOfOneSiteHoldsNothingInAnother()
    {
        var role = _database.Database.AddRoleForTest("A only", QueryDatabase.SiteA, ("content.article.create", null), ("content.article.read", null));

        await Should.NotThrowAsync(() => _database.CreateAsync(WithRole(role), Titled("Here")));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.CreateAsync(WithRole(role), Titled("There"), site: SiteB));
    }

    [Fact]
    public async Task AnonymousCannotWrite()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Hello"));
        var before = _database.Footprint();

        // Even when the Public role grants it: writing needs a scope that an anonymous principal never has.
        _database.Database.GrantForTest(BuiltInRoles.Public, "content.article.create");
        _database.Database.GrantForTest(BuiltInRoles.Public, "content.article.update");

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.CreateAsync(Principal.Anonymous, Titled("x")));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.UpdateAsync(Principal.Anonymous, IdOf(entry), Titled("x"), 1));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.PatchAsync(Principal.Anonymous, IdOf(entry), Titled("x"), 1));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.DeleteAsync(Principal.Anonymous, IdOf(entry)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.RestoreAsync(Principal.Anonymous, IdOf(entry)));

        _database.Footprint().ShouldBe(before);
    }

    [Fact]
    public async Task AnonymousReadsNothingUntilThePublicRoleAllowsIt()
    {
        await _database.CreateAsync(Editor, Titled("Hello"));

        (await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(Principal.Anonymous, new ContentQuery())))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("type");
        (await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(Principal.Anonymous, new ContentQuery { Status = QueryStatus.Draft })))
            .Errors.ShouldContain(error => error.Path == "type");
    }

    [Fact]
    public async Task WithNoOperationNothingRuns()
    {
        using var scope = _database.Database.NewScope();
        scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>().Current = null;
        var service = scope.ServiceProvider.GetRequiredService<IContentService>();

        await Should.ThrowAsync<InvalidOperationException>(() => service.CreateAsync("article", Titled("x"), Token));
        await Should.ThrowAsync<InvalidOperationException>(() => service.GetAsync("article", Guid.CreateVersion7(), null, Token));
    }

    [Theory]
    [InlineData(Origin.Api)]
    [InlineData(Origin.Admin)]
    [InlineData(Origin.Mcp)]
    public async Task TheSystemPrincipalIsRefusedFromAClientOrigin(Origin origin)
    {
        await Should.ThrowAsync<InvalidOperationException>(() => _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().CreateAsync("article", Titled("x"), Token),
            origin: origin));
    }

    [Theory]
    [InlineData(Origin.Job)]
    [InlineData(Origin.Cli)]
    [InlineData(Origin.Seed)]
    public async Task TheSystemPrincipalWritesFromAJobTheToolOrSeeding(Origin origin)
    {
        using var scope = _database.Database.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();

        using (SystemOperation.Begin(accessor, origin, SiteA))
        {
            accessor.Current!.Principal.ShouldBe(Principal.System);

            var entry = await scope.ServiceProvider.GetRequiredService<IContentService>().CreateAsync("article", new() { ["title"] = "Seeded", ["secret"] = "x" }, Token);

            entry["locale"].ShouldBe("en");
            entry.ContainsKey("secret").ShouldBeFalse();
            _database.Read(context => context.Documents.Single(document => document.DocumentId == IdOf(entry)).CreatedBy).ShouldBe(Guid.Empty);
            _database.Versions(IdOf(entry)).ShouldHaveSingleItem().Origin.ShouldBe(origin);
        }

        accessor.Current.ShouldBeNull();
    }

    [Fact]
    public void ASystemOperationCannotBeBegunFromAClientOrigin()
    {
        using var scope = _database.Database.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();

        Should.Throw<ArgumentOutOfRangeException>(() => SystemOperation.Begin(accessor, Origin.Api, SiteA));
        accessor.Current.ShouldBeNull();
    }

    [Fact]
    public async Task EntriesTheSystemWroteAreNoOnesOwn()
    {
        var entry = await _database.AsAsync(
            Principal.System,
            services => services.GetRequiredService<IContentService>().CreateAsync("article", Titled("Seeded"), Token),
            origin: Origin.Seed);

        (await _database.GetAsync(AuthorRole(), IdOf(entry))).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteHidesTheEntryAndRestoreBringsItBack()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Hello"));
        var id = IdOf(entry);

        await _database.DeleteAsync(Editor, id);

        (await _database.GetAsync(Editor, id)).ShouldBeNull();
        (await _database.QueryAsync(Editor)).Items.ShouldBeEmpty();
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.PatchAsync(Editor, id, Titled("x"), 1));
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.DeleteAsync(Editor, id));

        var restored = await _database.RestoreAsync(Editor, id);

        restored["title"].ShouldBe("Hello");
        restored["version"].ShouldBe(1);
        (await _database.GetAsync(Editor, id)).ShouldNotBeNull();
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.RestoreAsync(Editor, id));
    }

    [Fact]
    public async Task DeleteAndRestoreWorkOnEveryLocale()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Hello"));
        var id = IdOf(entry);
        await _database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");

        await _database.DeleteAsync(Editor, id, locale: "fr");
        (await _database.GetAsync(Editor, id, locale: "en")).ShouldBeNull();

        await _database.RestoreAsync(Editor, id);
        (await _database.GetAsync(Editor, id, new GetOptions { Status = QueryStatus.Draft, Fallback = LocaleFallback.None }, locale: "fr"))!["title"].ShouldBe("Bonjour");
    }

    [Fact]
    public async Task RestoreHonoursTheRowRule()
    {
        var role = _database.Database.AddRoleForTest(
            "Restorer",
            ("content.*.create", null),
            ("content.*.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }),
            ("content.*.delete", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }),
            ("content.*.restore", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }));
        var ann = WithRole(role, Guid.CreateVersion7());
        var bob = WithRole(role, Guid.CreateVersion7());

        var mine = await _database.CreateAsync(ann, Titled("Mine"));
        var theirs = await _database.CreateAsync(bob, Titled("Theirs"));
        await _database.DeleteAsync(ann, IdOf(mine));
        await _database.DeleteAsync(bob, IdOf(theirs));

        (await _database.RestoreAsync(ann, IdOf(mine)))["title"].ShouldBe("Mine");
        await Should.ThrowAsync<EntryNotFoundException>(() => _database.RestoreAsync(ann, IdOf(theirs)));
        (await _database.GetAsync(bob, IdOf(theirs))).ShouldBeNull();

        // The seeded Author may delete its own entries but has no restore grant at all.
        var author = AuthorRole(Guid.CreateVersion7());
        var own = await _database.CreateAsync(author, Titled("Own"));
        await _database.DeleteAsync(author, IdOf(own));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.RestoreAsync(author, IdOf(own)));
    }

    [Fact]
    public async Task DeletingNeedsTheGrantOnEveryLocale()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Hello"));
        await _database.UpdateAsync(Editor, IdOf(entry), Titled("Bonjour"), 0, locale: "fr");
        var role = _database.Database.AddRoleForTest("French", ("content.article.delete", new PermissionConstraints { Locales = ["fr"] }));

        await Should.ThrowAsync<ContentAccessDeniedException>(() => _database.DeleteAsync(WithRole(role), IdOf(entry), locale: "fr"));
        (await _database.GetAsync(Editor, IdOf(entry))).ShouldNotBeNull();
    }
}
