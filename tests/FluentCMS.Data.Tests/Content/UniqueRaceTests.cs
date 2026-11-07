using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// Writers that start together and want one key: one wins and every other gets what it would have got had it come
// second, never a database error. SQLite serialises writers, so here each loser is refused by the check before its
// insert. On PostgreSQL and SQL Server (#2711) a loser can reach the insert, which the database refuses, and the
// write runs again.
public sealed class UniqueRaceTests : IDisposable
{
    private const int Writers = 4;

    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private sealed class Recorder : IEntryChangeSink
    {
        public List<EntryChange> Changes { get; } = [];

        public ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    // Each writer runs on a thread and in a request of its own, all released at once. An outcome is null for a write that
    // succeeded and what it threw otherwise.
    private static async Task<List<Exception?>> RaceAsync(Func<int, Task> write, int writers = Writers)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Enumerable.Range(0, writers).Select(index => Task.Run<Exception?>(async () =>
        {
            await start.Task;
            try
            {
                await write(index);
                return null;
            }
#pragma warning disable CA1031 // The outcome is what the test asserts on.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                return exception;
            }
        })).ToList();

        start.SetResult();
        return [.. await Task.WhenAll(running)];
    }

    private static void ShouldBeRefusedAs(Exception? outcome, string path, string code)
    {
        var error = outcome.ShouldBeOfType<ValidationException>().Errors.ShouldHaveSingleItem();
        (error.Path, error.Code).ShouldBe((path, code));
    }

    private static void ShouldHaveOneWinner(List<Exception?> outcomes, Action<Exception?> loser)
    {
        outcomes.Count(outcome => outcome is null).ShouldBe(1);
        foreach (var outcome in outcomes.Where(outcome => outcome is not null))
        {
            loser(outcome);
        }
    }

    [Fact]
    public async Task SingletonCreatesThatRaceKeepOneEntryAndRefuseTheRestAsExisting()
    {
        var outcomes = await RaceAsync(index => _database.CreateAsync(Editor, new() { ["name"] = $"Settings {index}" }, "settings"));

        ShouldHaveOneWinner(outcomes, outcome => ShouldBeRefusedAs(outcome, "", ContentErrorCodes.SingletonExists));
        _database.Read(context => context.Documents.IgnoreQueryFilters().Count(document => document.Type == "settings")).ShouldBe(1);
        _database.Read(context => context.Rows("cms_settings").Count()).ShouldBe(1);
    }

    // What a loser that got past the check meets at the insert. A trashed singleton keeps its rows, so it keeps its place too.
    [Fact]
    public async Task TheDatabaseHoldsASingletonToOneEntryPerSiteTrashedOrNot()
    {
        var settings = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "One" }, "settings"));
        await _database.DeleteAsync(Editor, settings, "settings");

        void AddSharedRow(Guid site) => _database.Read(context =>
        {
            context.Rows("cms_settings").Add(new Dictionary<string, object> { [StorageNames.DocumentId] = Guid.CreateVersion7(), [StorageNames.SiteId] = site, [StorageNames.State] = ContentState.Draft });
            return context.SaveChanges();
        });

        var refused = Should.Throw<DbUpdateException>(() => AddSharedRow(SiteA.Value));
        _database.Database.Services.GetRequiredService<IDataProvider>().IsUniqueViolation(refused).ShouldBeTrue();
        Should.NotThrow(() => AddSharedRow(SiteB.Value));
        ShouldBeRefusedAs(await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, new() { ["name"] = "Two" }, "settings")), "", ContentErrorCodes.SingletonExists);
    }

    [Fact]
    public async Task LocaleAddsThatRaceKeepOneLocaleAndTheRestConflict()
    {
        var id = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Hello" }));

        var outcomes = await RaceAsync(index => _database.UpdateAsync(Editor, id, new() { ["title"] = $"Bonjour {index}" }, 0, locale: "fr"));

        ShouldHaveOneWinner(outcomes, outcome =>
        {
            var conflict = outcome.ShouldBeOfType<VersionConflictException>();
            (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((0, 1));
            conflict.Current.ShouldNotBeNull()["locale"].ShouldBe("fr");
        });
        _database.Read(context => context.Documents.Count(document => document.DocumentId == id && document.Locale == "fr")).ShouldBe(1);
        _database.Versions(id).Count(version => version.Locale == "fr").ShouldBe(1);
    }

    // An empty value is what an unfinished draft holds and drafts do not clash on it, so these drafts share one, and
    // the published state has room for one.
    [Fact]
    public async Task PublishesThatRaceForOneUniqueValuePublishOneAndRefuseTheRestAsNotUnique()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("slug", "slug", field => field.Unique())).Build();
        using var database = new ServiceDatabase(schema: schema);
        List<Guid> ids = [];
        for (var index = 0; index < Writers; index++)
        {
            ids.Add(IdOf(await database.CreateAsync(Editor, [], "page")));
        }

        var outcomes = await RaceAsync(index => database.PublishAsync(Editor, ids[index], type: "page"));

        ShouldHaveOneWinner(outcomes, outcome => ShouldBeRefusedAs(outcome, "slug", ValidationErrorCodes.NotUnique));
        database.Read(context => context.Documents.Count(document => document.PublishedVersion != null)).ShouldBe(1);
        database.Read(context => context.Rows("cms_page").Count(row => EF.Property<short>(row, StorageNames.State) == ContentState.Published)).ShouldBe(1);
    }

    // Drafts can share a value only after a race of their own, which SQLite does not allow, so this one is written as it would be.
    [Fact]
    public async Task PublishesOfDraftsThatShareAUniqueValueAreAllRefusedAsNotUnique()
    {
        var first = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "One", ["code"] = "c1" }));
        var second = IdOf(await _database.CreateAsync(Editor, new() { ["title"] = "Two", ["code"] = "c2" }));
        _database.Read(context => context.Rows("cms_article")
            .Where(row => EF.Property<Guid>(row, StorageNames.DocumentId) == second)
            .ExecuteUpdate(set => set.SetProperty(row => EF.Property<string>(row, "code"), "c1")));

        var outcomes = await RaceAsync(index => _database.PublishAsync(Editor, index % 2 == 0 ? first : second));

        outcomes.ShouldAllBe(outcome => outcome != null);
        foreach (var outcome in outcomes)
        {
            ShouldBeRefusedAs(outcome, "code", ValidationErrorCodes.NotUnique);
        }

        _database.Read(context => context.Documents.Count(document => document.PublishedVersion != null)).ShouldBe(0);
    }

    // The draft state has no unique index (spec 4.3), so a database that does not serialise writers may save more than
    // one of these; none of them fails another way.
    [Fact]
    public async Task DraftsThatRaceForOneUniqueValueAreSavedOrRefusedAsNotUnique()
    {
        var outcomes = await RaceAsync(index => _database.CreateAsync(Editor, new() { ["title"] = $"Draft {index}", ["code"] = "same" }));

        outcomes.ShouldContain(outcome => outcome == null);
        foreach (var outcome in outcomes.Where(outcome => outcome is not null))
        {
            ShouldBeRefusedAs(outcome, "code", ValidationErrorCodes.NotUnique);
        }
    }

    [Fact]
    public async Task SiteCreatesThatRaceForOneSlugKeepOneSiteAndRefuseTheRestAsTaken()
    {
        (int Sites, int Locales, int Roles) Rows() => _database.Read(context => (context.Set<Site>().Count(), context.Set<SiteLocale>().Count(), context.Set<Role>().Count()));
        var before = Rows();

        var outcomes = await RaceAsync(index => CreateSiteAsync($"Docs {index}"));

        ShouldHaveOneWinner(outcomes, outcome => ShouldBeRefusedAs(outcome, "slug", SiteErrorCodes.InvalidSite));
        _database.Read(context => context.Set<Site>().Count(site => site.Slug == "docs")).ShouldBe(1);
        Rows().ShouldBe((before.Sites + 1, before.Locales + 1, before.Roles + 1));
    }

    // The slug is free when the check reads it and taken when the insert runs: another instance holds it uncommitted.
    [Fact]
    public async Task ASiteCreateThatLosesTheSlugAtTheInsertIsRefusedAsTaken()
    {
        using var other = _database.Database.NewScope();
        var context = other.ServiceProvider.GetRequiredService<CmsDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        context.Add(new Site { Id = Guid.CreateVersion7(), Name = "Other", Slug = "docs", DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync(Token);

        // SQLite runs a command synchronously, so the create waits for the write lock on a thread of its own.
        var create = Task.Run(() => CreateSiteAsync("Docs"), Token);
        await Task.Delay(500, Token);
        await transaction.CommitAsync(Token);

        ShouldBeRefusedAs(await Should.ThrowAsync<ValidationException>(() => create), "slug", SiteErrorCodes.InvalidSite);
        _database.Read(context => context.Set<Site>().Single(site => site.Slug == "docs").Name).ShouldBe("Other");
    }

    private async Task CreateSiteAsync(string name)
    {
        using var scope = _database.Database.NewScope();
        await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync(name, "docs", "en", Token);
    }

    // What a writer that lost a race meets: a key another row already holds, here the slug of site A.
    private static async Task BreakAUniqueKeyAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<CmsDbContext>();
        var site = new Site { Id = Guid.CreateVersion7(), Name = "Copy", Slug = "a", DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow };
        context.Add(site);
        try
        {
            await context.SaveChangesAsync(Token);
        }
        finally
        {
            context.Entry(site).State = EntityState.Detached;
        }
    }

    [Fact]
    public async Task AWriteThatBreaksAUniqueKeyRunsOnceMoreInATransactionOfItsOwn()
    {
        var recorder = new Recorder();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IEntryChangeSink>(recorder));
        var attempts = 0;

        var id = await database.AsAsync(Editor, async services =>
        {
            var service = services.GetRequiredService<ContentService>();
            return await service.InTransactionAsync(
                async () =>
                {
                    var created = await service.CreateAsync("article", new() { ["title"] = $"Attempt {++attempts}" }, Token);
                    if (attempts == 1)
                    {
                        await BreakAUniqueKeyAsync(services);
                    }

                    return IdOf(created);
                },
                Token);
        });

        attempts.ShouldBe(2);
        database.Read(context => context.Documents.Select(document => document.DocumentId).ToList()).ShouldBe([id]);
        (await database.GetAsync(Editor, id))!["title"].ShouldBe("Attempt 2");
        recorder.Changes.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task AWriteThatBreaksAUniqueKeyAgainFailsWithTheDatabaseError()
    {
        var attempts = 0;

        await Should.ThrowAsync<DbUpdateException>(() => _database.AsAsync(Editor, async services =>
        {
            var service = services.GetRequiredService<ContentService>();
            return await service.InTransactionAsync(
                async () =>
                {
                    attempts++;
                    await service.CreateAsync("article", new() { ["title"] = "Again" }, Token);
                    await BreakAUniqueKeyAsync(services);
                    return true;
                },
                Token);
        }));

        attempts.ShouldBe(2);
        _database.Read(context => context.Documents.Count()).ShouldBe(0);
    }

    [Fact]
    public async Task AWriteIsNotRunAgainForAnotherDatabaseError()
    {
        var attempts = 0;

        await Should.ThrowAsync<DbUpdateException>(() => _database.AsAsync(Editor, async services =>
        {
            var service = services.GetRequiredService<ContentService>();
            return await service.InTransactionAsync<bool>(
                () =>
                {
                    attempts++;
                    throw new DbUpdateException("Not a unique key.");
                },
                Token);
        }));

        attempts.ShouldBe(1);
    }

    // Only the transaction's owner can run it again, and this one is the caller's.
    [Fact]
    public async Task AWriteInATransactionItDidNotBeginIsNotRunAgain()
    {
        var attempts = 0;

        await Should.ThrowAsync<DbUpdateException>(() => _database.AsAsync(Editor, async services =>
        {
            var service = services.GetRequiredService<ContentService>();
            await using var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token);
            return await service.InTransactionAsync(
                async () =>
                {
                    attempts++;
                    await BreakAUniqueKeyAsync(services);
                    return true;
                },
                Token);
        }));

        attempts.ShouldBe(1);
    }
}
