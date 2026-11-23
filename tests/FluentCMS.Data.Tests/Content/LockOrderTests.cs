using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Providers;
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
// ended. PostgreSQL and SQL Server would end such a cycle by deadlock detection, which the tests count. SQLite has one writer,
// so there the second waits for the first's commit whatever the order, and the tests show only that both end as they should.
public sealed class LockOrderTests
{
    private static ServiceDatabase WithGate(CommandGate gate, Deadlocks deadlocks) =>
        new(cms => cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(gate, deadlocks)));

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

    // A publish of French copies the shared row and sets the status of English, which a shared write of English locks before
    // French. Held once it has taken its French row, as it did after the copy before #3524.
    [Fact]
    public async Task APublishAndASharedWriteThroughAnotherLocaleDoNotDeadlock()
    {
        var gate = new CommandGate(command => TestProvider.IsUpdateOf(command.CommandText, "cms_document") && RowLocks.Names(command, "fr"));
        var deadlocks = new Deadlocks();
        using var database = WithGate(gate, deadlocks);
        var id = await ModifiedThroughTheSharedRowAsync(database);
        Task? write = null;
        gate.At = async () => write = await RowLocks.BesideAsync(database.Database.ConnectionString, () => database.PatchAsync(Editor, id, new() { ["views"] = 3 }, 3), Token);

        var published = await OutcomeAsync(database.AsAsync(Editor, services =>
        {
            gate.Context = services.GetRequiredService<CmsDbContext>();
            return services.GetRequiredService<IContentService>().PublishAsync("article", id, new LocaleWriteOptions { Locale = "fr", ExpectedVersion = 2 }, Token);
        }));

        gate.Passed.ShouldBeTrue();
        var wrote = await OutcomeAsync(write!);
        deadlocks.Count.ShouldBe(0);
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
        deadlocks.Count.ShouldBe(0);
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
        var gate = new CommandGate(command => TestProvider.IsUpdateOf(command.CommandText, "cms_document") && RowLocks.Names(command, "fr"));
        var deadlocks = new Deadlocks();
        using var database = WithGate(gate, deadlocks);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
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
        deadlocks.Count.ShouldBe(0);
        imported.ShouldBe(TransferOutcome.Updated);
        var conflict = wrote.ShouldBeOfType<VersionConflictException>();
        (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((2, 3));
        (await database.GetAsync(Editor, id, locale: "fr"))!["views"].ShouldBe(5L);
    }

    // A write decides its own status before its swap. What another request commits just before that swap, while the write
    // holds no row, moves what decides it without moving the version the swap compares, so the swap passes and the status is
    // decided again from what is stored. On SQLite the write lets the one write lock go for that request.
    [Theory]
    [InlineData("another locale's publish")]
    [InlineData("this locale's publish")]
    [InlineData("this locale's unpublish")]
    public async Task AWriteDecidesItsStatusFromWhatIsStoredAfterItsSwap(string between)
    {
        var gate = new CommandGate(command => TestProvider.IsUpdateOf(command.CommandText, "cms_document") && RowLocks.Names(command, "en"), after: false);
        using var database = WithGate(gate, new Deadlocks());

        Guid id;
        int version;
        ContentDocument patch;
        EntryStatus expected;
        Func<Task> request;
        switch (between)
        {
            case "another locale's publish":
                // Saved back as it is, English equals what is published once French has published the shared row.
                id = await ModifiedThroughTheSharedRowAsync(database);
                (version, patch, expected) = (3, new() { ["title"] = "Hello" }, EntryStatus.Published);
                request = () => database.PublishAsync(Editor, id, "fr", 2);
                break;
            case "this locale's publish":
                id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
                (version, patch, expected) = (1, new() { ["title"] = "Two" }, EntryStatus.Modified);
                request = () => database.PublishAsync(Editor, id, expected: 1);
                break;
            default:
                id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
                await database.PublishAsync(Editor, id);
                (version, patch, expected) = (1, new() { ["title"] = "Two" }, EntryStatus.Unpublished);
                request = () => database.UnpublishAsync(Editor, id);
                break;
        }

        var saved = await database.AsAsync(Editor, services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            gate.Context = context;
            gate.At = () => RowLocks.CommittedMeanwhileAsync(context, request);
            return services.GetRequiredService<IContentService>().PatchAsync("article", id, patch, version, Token);
        });

        gate.Passed.ShouldBeTrue();
        (database.Document(id).Status, saved["status"]).ShouldBe((expected, expected));
        database.Document(id).DraftVersion.ShouldBe(version + 1);
    }
}
