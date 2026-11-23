using System.Diagnostics;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Activity;
using FluentCMS.Data.Tests.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// #3573: a write that waits past the lock timeout for what another transaction holds gives up as busy, having changed
// nothing. The holder takes the entry's row as a hold does; on SQLite its transaction holds the database's one write lock.
public sealed class LockWaitTests
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(1);

    // Well under the drivers' 30 second command timeout, which a write gave up at before.
    private static readonly TimeSpan GaveUpWithin = TimeSpan.FromSeconds(15);

    private static ServiceDatabase Database() =>
        new(ActivityFixture.On(cms => cms.Services.Configure<ContentWriteOptions>(options => options.LockTimeout = LockTimeout)));

    // Another transaction that holds the row of the entry in English until it is disposed.
    private static async Task<IAsyncDisposable> HoldAsync(ServiceDatabase database, Guid id)
    {
        var row = database.Document(id);
        var scope = database.Database.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var transaction = await context.Database.BeginTransactionAsync(Token);
        await context.Documents.Where(document => document.Id == row.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(document => document.DraftVersion, document => document.DraftVersion), Token);
        return new Held(scope, transaction);
    }

    private sealed class Held(IServiceScope scope, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            scope.Dispose();
        }
    }

    private static int Outbox(ServiceDatabase database) => database.Read(context => context.Set<OutboxMessage>().Count());

    [Fact]
    public async Task AWriteThatWaitsPastTheLockTimeoutIsBusyAndChangesNothing()
    {
        using var database = Database();
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        await database.DrainAsync();
        var trace = database.Trace(id);
        var outbox = Outbox(database);

        EntryBusyException busy;
        var waited = Stopwatch.StartNew();
        await using (await HoldAsync(database, id))
        {
            busy = await Should.ThrowAsync<EntryBusyException>(() => database.PatchAsync(Editor, id, new() { ["title"] = "Two" }, 1).WaitAsync(RowLocks.Limit, Token));
            waited.Stop();
        }

        TestProvider.Offline().IsLockTimeout(busy.InnerException!).ShouldBeTrue(busy.InnerException!.ToString());
        waited.Elapsed.ShouldBeLessThan(GaveUpWithin);
        database.Trace(id).ShouldBe(trace);
        Outbox(database).ShouldBe(outbox);

        // Nothing of the write that gave up is left to get in the way of its retry.
        VersionOf(await database.PatchAsync(Editor, id, new() { ["title"] = "Two" }, 1)).ShouldBe(2);
    }

    // A publish holds every row of the entry, so a write of another locale waits for it too (#3524).
    [Fact]
    public async Task AWriteOfAnotherLocaleThatWaitsPastTheLockTimeoutIsBusy()
    {
        using var database = Database();
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        await database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
        var trace = database.Trace(id);

        await using (await HoldAsync(database, id))
        {
            await Should.ThrowAsync<EntryBusyException>(() => database.PublishAsync(Editor, id, "fr", 1).WaitAsync(RowLocks.Limit, Token));
        }

        database.Trace(id).ShouldBe(trace);
    }

    // SET LOCK_TIMEOUT stays with a SQL Server session, and the context keeps its session while its connection is open.
    [Fact]
    public async Task AWriteLeavesNoLockTimeoutOnAConnectionThatStaysOpen()
    {
        Assert.SkipWhen(TestProvider.IsSqlite, TestProvider.ServerOnlyReason("the lock timeout of a session"));
        using var database = Database();
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));

        var (written, gaveUp) = await database.AsAsync(Editor, async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            var content = services.GetRequiredService<IContentService>();
            await context.Database.OpenConnectionAsync(Token);
            await content.PatchAsync("article", id, new() { ["title"] = "Two" }, 1, Token);
            var written = await SessionLockTimeoutAsync(context);
            await using (await HoldAsync(database, id))
            {
                await Should.ThrowAsync<EntryBusyException>(() => content.PatchAsync("article", id, new() { ["title"] = "Three" }, 2, Token).WaitAsync(RowLocks.Limit, Token));
            }

            return (written, await SessionLockTimeoutAsync(context));
        });

        var none = TestProvider.IsPostgreSql ? "0" : "-1";
        (written, gaveUp).ShouldBe((none, none));
    }

    private static async Task<string> SessionLockTimeoutAsync(CmsDbContext context) =>
        (await context.Database.SqlQueryRaw<string>(TestProvider.IsPostgreSql
                ? """SELECT current_setting('lock_timeout') AS "Value" """
                : "SELECT CAST(@@LOCK_TIMEOUT AS nvarchar(20)) AS [Value]")
            .ToListAsync(Token)).Single();
}
