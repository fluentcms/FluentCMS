using System.Collections.Concurrent;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// #3571: a write holds its own entry's rows to its commit, hooks included, and what a hook writes takes its rows where it
// writes them, so two publishes whose after hooks save each other's entry hold one entry each and wait for the other's. There
// is no order between entries; PostgreSQL and SQL Server end the cycle by deadlock detection, and the victim runs again with
// its hooks (#2959). The winner's hook has saved the victim's entry by then, so a rerun that reads before the winner commits
// answers 412, as for any other request's commit between its read and its copy (#3536), and one that reads after publishes
// that draft. On SQL Server the plan of a small table can scan, and the second publish then waits for the first's new
// published row where its copy deletes its own, before its hook runs; the first's hook closes the cycle all the same. SQLite
// has one writer, so there the second publish waits for the first's commit and nothing crosses.
public sealed class CrossedHookTests
{
    private sealed class Crossing
    {
        private Func<Task<Task>>? _beside;

        public Guid First { get; set; }

        public Guid Second { get; set; }

        public string ConnectionString { get; set; } = "";

        public bool HoldTheWinner { get; set; }

        public ConcurrentDictionary<Guid, Task> Publishes { get; } = new();

        public ConcurrentQueue<Guid> Ran { get; } = new();

        // The entries whose publish's hook saved the other entry, in the order the saves returned: the winner first.
        public ConcurrentQueue<Guid> Saved { get; } = new();

        public void StartBeside(Func<Task<Task>> beside) => _beside = beside;

        public Func<Task<Task>>? TakeBeside() => Interlocked.Exchange(ref _beside, null);

        public Guid Other(Guid id) => id == First ? Second : First;
    }

    // The first publish's hook starts the second publish once it holds its own entry, and saves the second entry once the
    // server shows the second publish waiting, which its own hook's save of the first entry does, or on SQL Server its copy.
    // Each hook saves the other entry at the version it was created at, so a save stored twice would be a conflict. Held, the
    // winner's hook returns only once the server shows the victim's rerun waiting for the row the winner's save holds, so that
    // the rerun read before the winner's commit.
    private sealed class SavesTheOther(IContentService content, Crossing crossing) : IAfterPublishHook<ContentDocument>
    {
        public async ValueTask AfterPublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            var id = (Guid)context.Entry["id"]!;
            crossing.Ran.Enqueue(id);
            if (id == crossing.First && crossing.TakeBeside() is { } beside)
            {
                crossing.Publishes[crossing.Second] = await beside();
            }

            var other = crossing.Other(id);
            await content.PatchAsync("article", other, new() { ["subtitle"] = $"Saved by the publish of {id}" }, 1, cancellationToken);
            crossing.Saved.Enqueue(id);
            if (crossing.HoldTheWinner && crossing.Saved.Count == 1 && !TestProvider.IsSqlite)
            {
                await RowLocks.BesideAsync(crossing.ConnectionString, () => OutcomeAsync(crossing.Publishes[other]), cancellationToken);
            }
        }
    }

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

    // Whichever outcome the rerun meets, the winner published and its hook's save is stored once, nothing is stored twice and
    // nothing fails otherwise. A victim that published did so at the draft the winner's hook left, and its own hook's save is
    // stored once. A victim answered 412 with that draft's stored version, published nothing, and its hook's save rolled back
    // with it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoPublishesWhoseAfterHooksSaveEachOthersEntryEndOnceTheVictimRunsAgain(bool holdTheWinner)
    {
        var crossing = new Crossing { HoldTheWinner = holdTheWinner };
        var deadlocks = new Deadlocks();
        using var database = new ServiceDatabase(cms =>
        {
            cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(deadlocks));
            cms.Services.AddSingleton(crossing);
            cms.Services.AddScoped<IAfterPublishHook<ContentDocument>, SavesTheOther>();
        });
        crossing.ConnectionString = database.Database.ConnectionString;
        crossing.First = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "First" }));
        crossing.Second = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Second" }));
        crossing.StartBeside(() => RowLocks.BesideAsync(database.Database.ConnectionString, () => database.PublishAsync(Editor, crossing.Second), Token));

        var publish = database.PublishAsync(Editor, crossing.First);
        crossing.Publishes[crossing.First] = publish;
        var first = await OutcomeAsync(publish);
        var second = await OutcomeAsync(crossing.Publishes.GetValueOrDefault(crossing.Second).ShouldNotBeNull());

        deadlocks.Count.ShouldBe(TestProvider.IsSqlite ? 0 : 1);
        crossing.Saved.TryPeek(out var winner).ShouldBeTrue();
        var victim = crossing.Other(winner);
        var (won, lost) = winner == crossing.First ? (first, second) : (second, first);
        won.ShouldBeNull();
        crossing.Ran.Count(id => id == winner).ShouldBe(1);
        int SavesOf(Guid id, Guid by) => database.Versions(id).Count(version => version.Snapshot.Contains($"Saved by the publish of {by}", StringComparison.Ordinal));
        SavesOf(victim, winner).ShouldBe(1);
        database.Row("cms_article_i18n", victim, "en")["subtitle"].ShouldBe($"Saved by the publish of {winner}");
        var stored = database.Document(victim);
        stored.DraftVersion.ShouldBe(2);
        if (holdTheWinner && !TestProvider.IsSqlite)
        {
            lost.ShouldBeOfType<VersionConflictException>();
        }

        if (lost is null)
        {
            (stored.Status, stored.PublishedVersion).ShouldBe((EntryStatus.Published, (int?)2));
            database.PublishedRow("cms_article_i18n", victim, "en")!["subtitle"].ShouldBe($"Saved by the publish of {winner}");
            SavesOf(winner, victim).ShouldBe(1);
            var published = database.Document(winner);
            (published.Status, published.DraftVersion, published.PublishedVersion).ShouldBe((EntryStatus.Modified, 2, (int?)1));
        }
        else
        {
            var conflict = lost.ShouldBeOfType<VersionConflictException>();
            (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((1, stored.DraftVersion));
            (stored.Status, stored.PublishedVersion).ShouldBe((EntryStatus.Draft, (int?)null));
            database.PublishedRow("cms_article_i18n", victim, "en").ShouldBeNull();
            SavesOf(winner, victim).ShouldBe(0);
            var published = database.Document(winner);
            (published.Status, published.DraftVersion, published.PublishedVersion).ShouldBe((EntryStatus.Published, 1, (int?)1));
        }
    }
}
