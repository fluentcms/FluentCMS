using System.Data.Common;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Activity;
using FluentCMS.Data.Tests.Events;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Events;
using FluentCMS.Hooks;
using FluentCMS.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SQLitePCL;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// The second run of a write whose insert lost a race, which SQLite never reaches by itself: the first save of the write
// fails as PostgreSQL and SQL Server fail a loser that got past its checks, and a winner may commit before it runs again.
// Each case is measured against the same write on another entry that nothing interrupted.
public sealed class LostInsertTests
{
    private const string FromAHook = "From a hook";

    private static readonly SchemaModel Pages = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("slug", "slug", field => field.Unique())).Build();

    // Fails the next content save once, with the error of a unique key, and writes the winner, if there is one, just
    // before the write begins its transaction again, when it holds no lock.
    private sealed class LostInsert : ISaveChangesInterceptor, IDbTransactionInterceptor
    {
        private bool _armed;
        private bool _runAgain;
        private Func<Task>? _winner;

        public int Failures { get; private set; }

        public List<object>? TrackedWhenRunAgain { get; private set; }

        public void Arm(Func<Task>? winner = null)
        {
            _armed = true;
            _winner = winner;
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_armed)
            {
                return ValueTask.FromResult(result);
            }

            _armed = false;
            Failures++;
            throw new DbUpdateException("An error occurred while saving the entity changes.", new SqliteException("SQLite Error 19: 'UNIQUE constraint failed'.", raw.SQLITE_CONSTRAINT, raw.SQLITE_CONSTRAINT_UNIQUE));
        }

        public async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Failures == 1 && !_runAgain)
            {
                _runAgain = true;
                TrackedWhenRunAgain = [.. eventData.Context!.ChangeTracker.Entries().Select(entry => entry.Entity)];
                if (_winner is { } winner)
                {
                    await winner();
                }
            }

            return result;
        }
    }

    private sealed class Runs
    {
        public List<string> Notes { get; } = [];
    }

    // Tracks a row it never saves and raises an event, as a hook may. Neither may outlive a run that failed.
    private sealed class Busy(CmsDbContext context, IEventBus bus, Runs runs) : IBeforeCreateHook<ContentDocument>, IBeforeUpdateHook<ContentDocument>, IBeforePublishHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> hook, CancellationToken cancellationToken) => RunAsync("create", hook, cancellationToken);

        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> hook, CancellationToken cancellationToken) => RunAsync("update", hook, cancellationToken);

        public ValueTask BeforePublishAsync(HookContext<ContentDocument> hook, CancellationToken cancellationToken) => RunAsync("publish", hook, cancellationToken);

        private ValueTask RunAsync(string kind, HookContext<ContentDocument> hook, CancellationToken cancellationToken)
        {
            var note = Note(kind, (Guid)hook.Entry["id"]!, hook.Entry.GetValueOrDefault("title") ?? hook.Entry.GetValueOrDefault("name"));
            runs.Notes.Add(note);
            var id = Guid.CreateVersion7();
            context.Add(new Site { Id = id, Name = FromAHook, Slug = $"hook-{id:N}", DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow });
            return bus.PublishAsync(new Ping(note), cancellationToken);
        }
    }

    private static string Note(string kind, Guid id, object? name) => $"{kind}:{id}:{name}";

    private sealed record Setup(ServiceDatabase Database, LostInsert Lost, Seen Seen, Runs Runs) : IDisposable
    {
        public void Dispose() => Database.Dispose();
    }

    private static Setup Start(SchemaModel? schema = null)
    {
        var (lost, seen, runs) = (new LostInsert(), new Seen(), new Runs());
        var database = new ServiceDatabase(
            EventRegistrations.Outbox(seen, ActivityFixture.On(cms =>
            {
                cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(lost));
                cms.Services.AddSingleton(runs);
                cms.Services.AddScoped<IBeforeCreateHook<ContentDocument>, Busy>();
                cms.Services.AddScoped<IBeforeUpdateHook<ContentDocument>, Busy>();
                cms.Services.AddScoped<IBeforePublishHook<ContentDocument>, Busy>();
                cms.Events.AddHandler<Ping, PingHandler>();
                cms.Events.AddOutboxHandler<Ping, PingOutbox>("ping-outbox");
            })),
            schema);
        return new Setup(database, lost, seen, runs);
    }

    // What the write left in every table it writes, the outbox included, and the activity rows its outbox rows give.
    private static async Task<(T Result, Dictionary<string, int> Rows)> RowsOfAsync<T>(Setup setup, Func<Task<T>> write, params string[] tables)
    {
        Dictionary<string, int> Count() => setup.Database.Read(context =>
        {
            var rows = new Dictionary<string, int>
            {
                ["cms_document"] = context.Documents.IgnoreQueryFilters().Count(),
                ["cms_version"] = context.Versions.Count(),
                ["cms_reference"] = context.References.Count(),
                ["cms_outbox"] = context.Set<OutboxMessage>().Count(),
            };
            foreach (var table in tables)
            {
                rows[table] = context.Rows(table).Count();
            }

            return rows;
        });

        await setup.Database.DrainAsync();
        var before = Count();
        var activity = setup.Database.ActivityRows().Count;
        var result = await write();
        var after = Count();
        await setup.Database.DrainAsync();
        after["cms_activity"] = setup.Database.ActivityRows().Count - activity;
        return (result, after.ToDictionary(row => row.Key, row => row.Value - before.GetValueOrDefault(row.Key)));
    }

    // The write as a request whose caller has a row of its own pending on the context, what it threw, and what the context tracked after it.
    private sealed record Written(Exception? Outcome, List<(object Entity, EntityState State)> Tracked, Site Pending);

    private static async Task<Written> WriteAsync(Setup setup, Func<IContentService, Task> write, string locale = "en")
    {
        var pending = new Site { Id = Guid.CreateVersion7(), Name = "Pending", Slug = "pending", DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow };
        return await setup.Database.AsAsync(
            Editor,
            async services =>
            {
                var context = services.GetRequiredService<CmsDbContext>();
                context.Add(pending);
                Exception? outcome = null;
                try
                {
                    await write(services.GetRequiredService<IContentService>());
                }
#pragma warning disable CA1031 // The outcome is what the test asserts on.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    outcome = exception;
                }

                return new Written(outcome, [.. context.ChangeTracker.Entries().Select(entry => (entry.Entity, entry.State))], pending);
            },
            locale);
    }

    private static void ShouldHaveRunAgainOnTheContextAsItWas(Setup setup, Written loser, bool hooksRanAgain)
    {
        setup.Lost.Failures.ShouldBe(1);
        setup.Lost.TrackedWhenRunAgain.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBeSameAs(loser.Pending);
        loser.Tracked.ShouldContain((loser.Pending, EntityState.Added));
        loser.Tracked.Count(item => item.Entity is Site { Name: FromAHook }).ShouldBe(hooksRanAgain ? 1 : 0);
    }

    private static int Heard(Setup setup, string handler, Func<string, bool> note) =>
        setup.Seen.Calls.Zip(setup.Seen.Events).Count(pair => pair.First == handler && pair.Second is Ping ping && note(ping.Note));

    private static void ShouldBeHeardOnce(Setup setup, Func<string, bool> note)
    {
        Heard(setup, "ping", note).ShouldBe(1);
        Heard(setup, "ping-outbox", note).ShouldBe(1);
    }

    private static void ShouldBeRefusedAs(Exception? outcome, string path, string code)
    {
        var error = outcome.ShouldBeOfType<ValidationException>().Errors.ShouldHaveSingleItem();
        (error.Path, error.Code).ShouldBe((path, code));
    }

    [SqliteFact("a lost insert faked with SQLite's error")]
    public async Task ASingletonCreateThatLosesAtTheInsertIsRefusedAsTheWinnersEntryExists()
    {
        using var setup = Start();
        var (_, alone) = await RowsOfAsync(setup, () => setup.Database.CreateAsync(Editor, new() { ["name"] = "Alone" }, "settings", site: SiteB), "cms_settings");
        Guid winner = default;
        setup.Lost.Arm(async () => winner = IdOf(await setup.Database.CreateAsync(Editor, new() { ["name"] = "Winner" }, "settings")));

        var (loser, raced) = await RowsOfAsync(setup, () => WriteAsync(setup, content => content.CreateAsync("settings", new() { ["name"] = "Loser" }, Token)), "cms_settings");

        ShouldBeRefusedAs(loser.Outcome, "", ContentErrorCodes.SingletonExists);
        ShouldHaveRunAgainOnTheContextAsItWas(setup, loser, hooksRanAgain: false);
        raced.ShouldBe(alone);
        setup.Database.Read(context => context.Documents.Where(document => document.Type == "settings" && document.SiteId == SiteA.Value).Select(document => document.DocumentId).ToList()).ShouldBe([winner]);
        setup.Runs.Notes.Count(note => note.EndsWith(":Loser", StringComparison.Ordinal)).ShouldBe(1);
        Heard(setup, "ping", note => note.EndsWith(":Loser", StringComparison.Ordinal)).ShouldBe(0);
        Heard(setup, "ping-outbox", note => note.EndsWith(":Loser", StringComparison.Ordinal)).ShouldBe(0);
        ShouldBeHeardOnce(setup, note => note == Note("create", winner, "Winner"));
    }

    [SqliteFact("a lost insert faked with SQLite's error")]
    public async Task ASingletonCreateThatLosesAtTheInsertWithNoWinnerIsWrittenOnce()
    {
        using var setup = Start();
        var (_, alone) = await RowsOfAsync(setup, () => setup.Database.CreateAsync(Editor, new() { ["name"] = "Alone" }, "settings", site: SiteB), "cms_settings");
        setup.Lost.Arm();

        var (loser, raced) = await RowsOfAsync(setup, () => WriteAsync(setup, content => content.CreateAsync("settings", new() { ["name"] = "Again" }, Token)), "cms_settings");

        loser.Outcome.ShouldBeNull();
        ShouldHaveRunAgainOnTheContextAsItWas(setup, loser, hooksRanAgain: true);
        raced.ShouldBe(alone);
        setup.Runs.Notes.Count(note => note.EndsWith(":Again", StringComparison.Ordinal)).ShouldBe(2);
        ShouldBeHeardOnce(setup, note => note.EndsWith(":Again", StringComparison.Ordinal));
    }

    [SqliteFact("a lost insert faked with SQLite's error")]
    public async Task ALocaleAddThatLosesAtTheInsertConflictsWithTheWinnersLocale()
    {
        using var setup = Start();
        var other = IdOf(await setup.Database.CreateAsync(Editor, new() { ["title"] = "Other" }));
        var id = IdOf(await setup.Database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        var author = IdOf(await setup.Database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var (_, alone) = await RowsOfAsync(setup, () => setup.Database.UpdateAsync(Editor, other, new() { ["title"] = "Seul", ["sponsor"] = author }, 0, locale: "fr"), "cms_article", "cms_article_i18n");
        setup.Lost.Arm(() => setup.Database.UpdateAsync(Editor, id, new() { ["title"] = "Gagnant", ["sponsor"] = author }, 0, locale: "fr"));

        var (loser, raced) = await RowsOfAsync(setup, () => WriteAsync(setup, content => content.UpdateAsync("article", id, new() { ["title"] = "Perdant", ["sponsor"] = author }, 0, Token), "fr"), "cms_article", "cms_article_i18n");

        var conflict = loser.Outcome.ShouldBeOfType<VersionConflictException>();
        (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((0, 1));
        conflict.Current.ShouldNotBeNull()["title"].ShouldBe("Gagnant");
        ShouldHaveRunAgainOnTheContextAsItWas(setup, loser, hooksRanAgain: false);
        raced.ShouldBe(alone);
        setup.Runs.Notes.Count(note => note == Note("update", id, "Perdant")).ShouldBe(1);
        Heard(setup, "ping", note => note == Note("update", id, "Perdant")).ShouldBe(0);
        Heard(setup, "ping-outbox", note => note == Note("update", id, "Perdant")).ShouldBe(0);
        ShouldBeHeardOnce(setup, note => note == Note("update", id, "Gagnant"));
    }

    [SqliteFact("a lost insert faked with SQLite's error")]
    public async Task ALocaleAddThatLosesAtTheInsertWithNoWinnerIsWrittenOnce()
    {
        using var setup = Start();
        var other = IdOf(await setup.Database.CreateAsync(Editor, new() { ["title"] = "Other" }));
        var id = IdOf(await setup.Database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        var author = IdOf(await setup.Database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        var (_, alone) = await RowsOfAsync(setup, () => setup.Database.UpdateAsync(Editor, other, new() { ["title"] = "Seul", ["sponsor"] = author }, 0, locale: "fr"), "cms_article", "cms_article_i18n");
        setup.Lost.Arm();

        var (loser, raced) = await RowsOfAsync(setup, () => WriteAsync(setup, content => content.UpdateAsync("article", id, new() { ["title"] = "Encore", ["sponsor"] = author }, 0, Token), "fr"), "cms_article", "cms_article_i18n");

        loser.Outcome.ShouldBeNull();
        ShouldHaveRunAgainOnTheContextAsItWas(setup, loser, hooksRanAgain: true);
        raced.ShouldBe(alone);
        raced["cms_reference"].ShouldBe(1);
        setup.Runs.Notes.Count(note => note == Note("update", id, "Encore")).ShouldBe(2);
        ShouldBeHeardOnce(setup, note => note == Note("update", id, "Encore"));
    }

    // An empty value is what an unfinished draft holds, so both drafts may hold it, and the published state has room for one.
    [SqliteFact("a lost insert faked with SQLite's error")]
    public async Task APublishThatLosesAtTheInsertIsRefusedAsTheWinnerPublishedTheValue()
    {
        using var setup = Start(Pages);
        var other = IdOf(await setup.Database.CreateAsync(Editor, new() { ["slug"] = "other" }, "page"));
        var winner = IdOf(await setup.Database.CreateAsync(Editor, [], "page"));
        var id = IdOf(await setup.Database.CreateAsync(Editor, [], "page"));
        var (_, alone) = await RowsOfAsync(setup, () => setup.Database.PublishAsync(Editor, other, type: "page"), "cms_page");
        setup.Lost.Arm(() => setup.Database.PublishAsync(Editor, winner, type: "page"));

        var (loser, raced) = await RowsOfAsync(setup, () => WriteAsync(setup, content => content.PublishAsync("page", id, cancellationToken: Token)), "cms_page");

        ShouldBeRefusedAs(loser.Outcome, "slug", ValidationErrorCodes.NotUnique);
        ShouldHaveRunAgainOnTheContextAsItWas(setup, loser, hooksRanAgain: false);
        raced.ShouldBe(alone);
        setup.Database.Read(context => context.Documents.Where(document => document.PublishedVersion != null).Select(document => document.DocumentId).Order().ToList()).ShouldBe(new[] { other, winner }.Order().ToList());
        setup.Runs.Notes.Count(note => note == Note("publish", id, null)).ShouldBe(1);
        Heard(setup, "ping", note => note == Note("publish", id, null)).ShouldBe(0);
        Heard(setup, "ping-outbox", note => note == Note("publish", id, null)).ShouldBe(0);
        ShouldBeHeardOnce(setup, note => note == Note("publish", winner, null));
    }

    [SqliteFact("a lost insert faked with SQLite's error")]
    public async Task APublishThatLosesAtTheInsertWithNoWinnerIsWrittenOnce()
    {
        using var setup = Start(Pages);
        var other = IdOf(await setup.Database.CreateAsync(Editor, new() { ["slug"] = "other" }, "page"));
        var id = IdOf(await setup.Database.CreateAsync(Editor, new() { ["slug"] = "mine" }, "page"));
        var (_, alone) = await RowsOfAsync(setup, () => setup.Database.PublishAsync(Editor, other, type: "page"), "cms_page");
        setup.Lost.Arm();

        var (loser, raced) = await RowsOfAsync(setup, () => WriteAsync(setup, content => content.PublishAsync("page", id, cancellationToken: Token)), "cms_page");

        loser.Outcome.ShouldBeNull();
        ShouldHaveRunAgainOnTheContextAsItWas(setup, loser, hooksRanAgain: true);
        raced.ShouldBe(alone);
        setup.Database.Document(id, "").PublishedVersion.ShouldBe(1);
        setup.Runs.Notes.Count(note => note == Note("publish", id, null)).ShouldBe(2);
        ShouldBeHeardOnce(setup, note => note == Note("publish", id, null));
    }
}
