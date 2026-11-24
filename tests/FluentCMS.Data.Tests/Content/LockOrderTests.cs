using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using FluentCMS.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// #3524: writes, publishes and scheduled jobs of one entry take its rows in one order, so two that cross wait for each other.
// Each test holds the first at the point where, in another order, it would hold one row the second needs and want another the
// second holds, starts the second there and lets the first go once the server shows the second waiting, or the second has
// ended. PostgreSQL and SQL Server would end such a cycle by deadlock detection, which the tests count and describe. SQLite has
// one writer, so there the second waits for the first's commit whatever the order, and the tests show only that both end as
// they should.
public sealed class LockOrderTests
{
    private static ServiceDatabase WithGate(CommandGate gate, Deadlocks deadlocks, Action<FluentCMS.Core.DependencyInjection.FluentCmsBuilder>? more = null) =>
        new(cms =>
        {
            cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(gate, deadlocks));
            more?.Invoke(cms);
        });

    private static async Task<Exception?> OutcomeAsync(Task request)
    {
        try
        {
            await request.WaitAsync(RowLocks.Limit, Token);
            return null;
        }
#pragma warning disable CA1031 // The outcome is what the test asserts on.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return exception;
        }
    }

    // English published with views 1, French added, then views 2 through English, so English is Modified until a publish of
    // French copies the shared row. Answers English at version 3 and French at version 2.
    private static async Task<Guid> ModifiedThroughTheSharedRowAsync(ServiceDatabase database)
    {
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await database.PublishAsync(Editor, id);
        await database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
        await database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 2);
        return id;
    }

    // A statement that locks that row of cms_document, by its primary key or, as before #3524, by its document id and locale.
    private static Func<System.Data.Common.DbCommand, bool> Locks(Document? row) =>
        command => row is not null && TestProvider.IsUpdateOf(command.CommandText, "cms_document")
            && (RowLocks.Names(command, row.Id) || (RowLocks.Names(command, row.DocumentId) && RowLocks.Names(command, row.Locale)));

    // A publish of French copies the shared row and sets the status of English, which a shared write of English locks before
    // French. Held once it has taken its French row, as it did after the copy before #3524.
    [Fact]
    public async Task APublishAndASharedWriteThroughAnotherLocaleDoNotDeadlock()
    {
        Document? french = null;
        var gate = new CommandGate(command => Locks(french)(command));
        var deadlocks = new Deadlocks();
        using var database = WithGate(gate, deadlocks);
        var id = await ModifiedThroughTheSharedRowAsync(database);
        french = database.Document(id, "fr");
        Task? write = null;
        gate.At = async () => write = await RowLocks.BesideAsync(database.Database.ConnectionString, () => database.PatchAsync(Editor, id, new() { ["views"] = 3 }, 3), Token);

        var published = await OutcomeAsync(database.AsAsync(Editor, services =>
        {
            gate.Context = services.GetRequiredService<CmsDbContext>();
            return services.GetRequiredService<IContentService>().PublishAsync("article", id, new LocaleWriteOptions { Locale = "fr", ExpectedVersion = 2 }, Token);
        }));

        gate.Passed.ShouldBeTrue();
        var wrote = await OutcomeAsync(write!);
        await deadlocks.ShouldBeNoneAsync(database.Database.ConnectionString);
        published.ShouldBeNull();
        wrote.ShouldBeNull();
        database.PublishedRow("cms_article", id)!["views"].ShouldBe(2L);
        (database.Document(id, "en").Status, database.Document(id, "fr").Status).ShouldBe((EntryStatus.Modified, EntryStatus.Modified));
    }

    // A scheduled job ends its schedule and clears its mirror on the row, which a write that makes the schedule stale locks
    // before it ends it. Held once it has ended the schedule, as it did before the row before #3524.
    [Fact]
    public async Task AScheduledPublishAndAWriteThatEndsItsScheduleDoNotDeadlock()
    {
        var gate = new CommandGate(command => TestProvider.IsUpdateOf(command.CommandText, "cms_schedule"));
        var deadlocks = new Deadlocks();
        using var fixture = new SchedulingFixture(more: cms => cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(gate, deadlocks)));
        var user = fixture.Publisher("content.article.update");
        var id = await fixture.DraftAsync();
        await fixture.ScheduleAsync(user, id, SchedulingFixture.At(SchedulingFixture.Start.AddHours(1)));
        var row = fixture.Schedules(id).Single();
        Task? write = null;
        gate.At = async () => write = await RowLocks.BesideAsync(fixture.Database.Database.ConnectionString, () => fixture.Database.PatchAsync(Editor, id, new() { ["title"] = "Two" }, 1), Token);

        var outcome = await fixture.RunAsync(row.Id, row.JobId!.Value, user, prepare: services => gate.Context = services.GetRequiredService<CmsDbContext>()).WaitAsync(RowLocks.Limit, Token);

        gate.Passed.ShouldBeTrue();
        var wrote = await OutcomeAsync(write!);
        await deadlocks.ShouldBeNoneAsync(fixture.Database.Database.ConnectionString);
        outcome.ShouldBe(ScheduleOutcome.Executed);
        wrote.ShouldBeNull();
        fixture.Schedules(id).Single().Status.ShouldBe(ScheduleRow.Executed);
        var document = fixture.Database.Document(id);
        (document.Status, document.DraftVersion, document.PublishedVersion).ShouldBe((EntryStatus.Modified, 2, 1));
    }

    // An import writes the locales of its package in the package's order, here French before English, and a shared write of
    // English locks English before French. Held once it has taken its French row, as it did before English before #3524.
    [Fact]
    public async Task AnImportAndASharedWriteOfOneOfItsLocalesDoNotDeadlock()
    {
        Document? french = null;
        var gate = new CommandGate(command => Locks(french)(command));
        var deadlocks = new Deadlocks();
        using var database = WithGate(gate, deadlocks);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
        french = database.Document(id, "fr");
        var later = DateTimeOffset.UtcNow.AddHours(1);
        TransferEntryLocale Locale(string locale, string title) =>
            new(locale, EntryStatus.Draft, 5, null, later, Guid.CreateVersion7(), later, Guid.CreateVersion7(), null, null, new JsonObject { ["title"] = title, ["views"] = 5 }, null);
        var entry = new TransferEntry(id, [Locale("fr", "Salut"), Locale("en", "Hi")]);
        Task? write = null;
        gate.At = async () => write = await RowLocks.BesideAsync(database.Database.ConnectionString, () => database.PatchAsync(Editor, id, new() { ["views"] = 9 }, 2), Token);

        var imported = await database.AsAsync(
            Principal.System,
            services =>
            {
                services.GetRequiredService<TransferImportMode>().Active = true;
                gate.Context = services.GetRequiredService<CmsDbContext>();
                return services.GetRequiredService<ContentService>().ImportAsync("article", entry, overwrite: true, authorsFromImporter: false, Token);
            },
            origin: Origin.Import).WaitAsync(RowLocks.Limit, Token);

        gate.Passed.ShouldBeTrue();
        var wrote = await OutcomeAsync(write!);
        await deadlocks.ShouldBeNoneAsync(database.Database.ConnectionString);
        imported.ShouldBe(TransferOutcome.Updated);
        var conflict = wrote.ShouldBeOfType<VersionConflictException>();
        (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((2, 3));
        (await database.GetAsync(Editor, id, locale: "fr"))!["views"].ShouldBe(5L);
    }

    // A write decides its own status before its swap. What another request commits just before that swap, while the write
    // holds no row, moves what decides it without moving the version the swap compares, so the swap passes and the status is
    // decided again from what is stored. On SQLite the write lets the one write lock go for that request.
    [Theory]
    [InlineData("another locale's publish", "patch", EntryStatus.Published)]
    [InlineData("another locale's publish", "restore", EntryStatus.Published)]
    [InlineData("this locale's publish", "patch", EntryStatus.Modified)]
    [InlineData("this locale's publish", "restore", EntryStatus.Published)]
    [InlineData("this locale's unpublish", "patch", EntryStatus.Unpublished)]
    [InlineData("this locale's unpublish", "restore", EntryStatus.Unpublished)]
    [InlineData("this locale's publish and unpublish", "patch", EntryStatus.Unpublished)]
    [InlineData("this locale's publish and unpublish", "restore", EntryStatus.Unpublished)]
    public async Task AWriteDecidesItsStatusFromWhatIsStoredAfterItsSwap(string between, string write, EntryStatus expected)
    {
        Document? english = null;
        var gate = new CommandGate(command => Locks(english)(command), after: false);
        using var database = WithGate(gate, new Deadlocks());

        Guid id;
        int version;
        Func<Task> request;
        switch (between)
        {
            case "another locale's publish":
                // Saved back as it is, English equals what is published once French has published the shared row.
                id = await ModifiedThroughTheSharedRowAsync(database);
                version = 3;
                request = () => database.PublishAsync(Editor, id, "fr", 2);
                break;
            case "this locale's publish":
                id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
                version = 1;
                request = () => database.PublishAsync(Editor, id, expected: 1);
                break;
            case "this locale's unpublish":
                id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
                await database.PublishAsync(Editor, id);
                version = 1;
                request = () => database.UnpublishAsync(Editor, id);
                break;
            default:
                id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
                version = 1;
                request = async () =>
                {
                    await database.PublishAsync(Editor, id, expected: 1);
                    await database.UnpublishAsync(Editor, id);
                };
                break;
        }

        english = database.Document(id);
        var saved = await database.AsAsync(Editor, services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            gate.Context = context;
            gate.At = () => RowLocks.CommittedMeanwhileAsync(context, request);
            var content = services.GetRequiredService<IContentService>();
            return write == "patch"
                ? content.PatchAsync("article", id, new() { ["title"] = between == "another locale's publish" ? "Hello" : "Two" }, version, Token)
                : content.RestoreVersionAsync("article", id, version, new LocaleWriteOptions { ExpectedVersion = version }, Token);
        });

        gate.Passed.ShouldBeTrue();
        (database.Document(id).Status, saved["status"]).ShouldBe((expected, expected));
        database.Document(id).DraftVersion.ShouldBe(version + 1);
    }

    // A discard writes what is published, so it holds its row before it reads that, and is a conflict when another request
    // changed that row between the discard's read and its hold: a save of its draft (#3524, a discard whose draft equalled
    // the published one set Published over it), or a publish or an unpublish that moved what is published.
    [Theory]
    [InlineData("a save of this locale")]
    [InlineData("this locale's unpublish")]
    [InlineData("another locale's publish")]
    public async Task ADiscardIsAConflictWhenItsRowMovedBeforeItHeldIt(string between)
    {
        Document? english = null;
        var gate = new CommandGate(command => Locks(english)(command), after: false);
        using var database = WithGate(gate, new Deadlocks());

        Guid id;
        Func<Task> request;
        switch (between)
        {
            case "a save of this locale":
                // A draft equal to what is published that reads Modified, as a status left behind would.
                id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
                await database.PublishAsync(Editor, id);
                database.Read(context => context.Documents.Where(document => document.DocumentId == id).ExecuteUpdate(set => set.SetProperty(document => document.Status, EntryStatus.Modified)));
                request = () => database.PatchAsync(Editor, id, new() { ["title"] = "Two" }, 1);
                break;
            case "this locale's unpublish":
                id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
                await database.PublishAsync(Editor, id);
                await database.PatchAsync(Editor, id, new() { ["title"] = "Two" }, 1);
                request = () => database.UnpublishAsync(Editor, id);
                break;
            default:
                id = await ModifiedThroughTheSharedRowAsync(database);
                request = () => database.PublishAsync(Editor, id, "fr", 2);
                break;
        }

        english = database.Document(id);
        string? after = null;
        await Should.ThrowAsync<VersionConflictException>(() => database.AsAsync(Editor, services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            gate.Context = context;
            gate.At = () => RowLocks.CommittedMeanwhileAsync(context, async () =>
            {
                await request();
                after = database.Trace(id);
            });
            return services.GetRequiredService<IContentService>().DiscardDraftAsync("article", id, null, Token);
        }));

        gate.Passed.ShouldBeTrue();
        database.Trace(id).ShouldBe(after);
    }

    // What another request commits once, in the before hook of a write, and the locales each run of that hook was given.
    private sealed class Meanwhile
    {
        public Func<Task>? Run { get; set; }

        public List<string> Locales { get; } = [];

        public async ValueTask RunAsync(IServiceProvider services, IReadOnlyList<string> locales)
        {
            Locales.Add(string.Join(',', locales));
            if (Run is { } run)
            {
                Run = null;
                await RowLocks.CommittedMeanwhileAsync(services.GetRequiredService<CmsDbContext>(), run);
            }
        }
    }

    private sealed class MeanwhileUnpublish(IServiceProvider services, Meanwhile meanwhile) : IBeforeUnpublishHook<ContentDocument>
    {
        public ValueTask BeforeUnpublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => meanwhile.RunAsync(services, context.Locales);
    }

    private sealed class MeanwhileDelete(IServiceProvider services, Meanwhile meanwhile) : IBeforeDeleteHook<ContentDocument>
    {
        public ValueTask BeforeDeleteAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => meanwhile.RunAsync(services, context.Locales);
    }

    // Whether the shared published row goes with a locale is read under the holds of every row of the entry, which a publish
    // of another locale takes too: read before, a publish of English that commits in the hook of an unpublish of French was
    // not seen and its shared row went, and an unpublish of English that does was not seen and left the row behind.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnpublishDecidesWhetherItIsTheLastPublishedLocaleUnderItsHolds(bool englishPublished)
    {
        var meanwhile = new Meanwhile();
        using var database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton(meanwhile);
            cms.Services.AddScoped<IBeforeUnpublishHook<ContentDocument>, MeanwhileUnpublish>();
        });
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
        await database.PublishAsync(Editor, id, "fr");
        if (englishPublished)
        {
            await database.PublishAsync(Editor, id);
        }

        meanwhile.Run = englishPublished ? () => database.UnpublishAsync(Editor, id) : () => database.PublishAsync(Editor, id);

        await database.UnpublishAsync(Editor, id, "fr");

        meanwhile.Run.ShouldBeNull();
        database.Document(id, "fr").PublishedVersion.ShouldBeNull();
        if (englishPublished)
        {
            database.PublishedRow("cms_article", id).ShouldBeNull();
            database.References(id, ContentState.Published).ShouldBeEmpty();
        }
        else
        {
            database.Document(id).Status.ShouldBe(EntryStatus.Published);
            database.PublishedRow("cms_article", id)!["views"].ShouldBe(1L);
        }
    }

    private sealed class Changes : IEntryChangeSink
    {
        public List<EntryChange> Seen { get; } = [];

        public ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            Seen.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    // A trash holds the rows it read, after its hooks ran, and then marks every row of the entry. A locale added between
    // would be trashed unheld, unchecked and unannounced, so the trash reads the rows again under its holds and runs again
    // with the new one: its hooks see it, and it has its event.
    [Fact]
    public async Task ATrashThatALocaleWasAddedUnderRunsAgainWithIt()
    {
        var meanwhile = new Meanwhile();
        var changes = new Changes();
        using var database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton(meanwhile);
            cms.Services.AddSingleton<IEntryChangeSink>(changes);
            cms.Services.AddScoped<IBeforeDeleteHook<ContentDocument>, MeanwhileDelete>();
        });
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        await database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
        meanwhile.Run = () => database.UpdateAsync(Editor, id, new() { ["title"] = "Allo" }, 0, locale: "fr-CA");
        changes.Seen.Clear();

        await database.DeleteAsync(Editor, id);

        meanwhile.Locales.ShouldBe(["en,fr", "en,fr,fr-CA"]);
        changes.Seen.Where(change => change.Kind == EntryChangeKind.Trashed).Select(change => change.Locale).Order(StringComparer.Ordinal).ShouldBe(["en", "fr", "fr-CA"]);
        database.Read(context => context.Documents.IgnoreQueryFilters().Count(document => document.DocumentId == id && document.DeletedAt != null)).ShouldBe(3);
    }
}
