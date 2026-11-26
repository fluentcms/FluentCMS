using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// #3575: a hold (IDataProvider.HoldDocumentAsync) locks a row of cms_document by its primary key and changes nothing. On
// PostgreSQL it is a locking read, FOR NO KEY UPDATE, which writes no row version; elsewhere an update that sets nothing new.
public sealed class DocumentHoldTests
{
    // A transaction on a context of its own, which holds what it locked until it is disposed.
    private sealed class Holder : IAsyncDisposable
    {
        private readonly IServiceScope _scope;

        private Holder(IServiceScope scope, IDbContextTransaction transaction)
        {
            _scope = scope;
            Transaction = transaction;
        }

        public CmsDbContext Context => _scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        public IDataProvider Provider => _scope.ServiceProvider.GetRequiredService<IDataProvider>();

        public IDbContextTransaction Transaction { get; }

        public static async Task<Holder> BeginAsync(ServiceDatabase database, TimeSpan? lockTimeout = null)
        {
            var scope = database.Database.NewScope();
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var provider = scope.ServiceProvider.GetRequiredService<IDataProvider>();
            var transaction = lockTimeout is { } timeout
                ? await provider.BeginWriteAsync(context, timeout, Token)
                : await context.Database.BeginTransactionAsync(Token);
            return new Holder(scope, transaction);
        }

        public Task<bool> HoldAsync(Guid row, int? draftVersion = null) => Provider.HoldDocumentAsync(Context, row, draftVersion, Token);

        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            _scope.Dispose();
        }
    }

    [Fact]
    public async Task AHoldAnswersWhetherItLockedARowAndChangesNothing()
    {
        using var database = new ServiceDatabase();
        var live = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        var trashed = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Gone" }));
        await database.DeleteAsync(Editor, trashed);
        var row = database.Document(live);
        var inTrash = database.Document(trashed);

        List<bool> held;
        await using (var holder = await Holder.BeginAsync(database))
        {
            held =
            [
                await holder.HoldAsync(row.Id),
                await holder.HoldAsync(row.Id, row.DraftVersion),
                await holder.HoldAsync(row.Id, row.DraftVersion + 1),
                await holder.HoldAsync(inTrash.Id),
                await holder.HoldAsync(inTrash.Id, inTrash.DraftVersion),
                await holder.HoldAsync(Guid.CreateVersion7()),
                await holder.HoldAsync(Guid.CreateVersion7(), 1),
            ];
            await holder.Transaction.CommitAsync(Token);
        }

        held.ShouldBe([true, true, false, true, false, false, false]);
        var after = database.Document(live);
        (after.DraftVersion, after.Status, after.UpdatedAt, after.UpdatedBy).ShouldBe((row.DraftVersion, row.Status, row.UpdatedAt, row.UpdatedBy));
    }

    [PostgreSqlFact("a hold that writes no row version")]
    public async Task AHoldOnPostgreSqlWritesNoRowVersion()
    {
        using var database = new ServiceDatabase();
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        var row = database.Document(id);
        var before = RowVersion(database, row.Id);

        await using (var holder = await Holder.BeginAsync(database))
        {
            (await holder.HoldAsync(row.Id)).ShouldBeTrue();
            (await holder.HoldAsync(row.Id, row.DraftVersion)).ShouldBeTrue();
            await holder.Transaction.CommitAsync(Token);
        }

        RowVersion(database, row.Id).ShouldBe(before);
    }

    // The row's version, by the transaction that wrote it and where it is: an update, even of nothing, changes both.
    private static string RowVersion(ServiceDatabase database, Guid row) =>
        database.Read(context => context.Database.SqlQuery<string>($"""SELECT xmin::text || ' ' || ctid::text AS "Value" FROM cms_document WHERE id = {row}""").Single());

    // FOR NO KEY UPDATE, the lock of an update of a column outside every key: another update of the row waits for it, while a
    // read and the FOR KEY SHARE of a foreign key's check do not.
    [PostgreSqlFact("the row lock of a locking read")]
    public async Task AHoldOnPostgreSqlMakesAnUpdateWaitAndNotARead()
    {
        using var database = new ServiceDatabase();
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" }));
        var row = database.Document(id);

        Task update;
        await using (var holder = await Holder.BeginAsync(database))
        {
            (await holder.HoldAsync(row.Id)).ShouldBeTrue();

            database.Read(context => context.Documents.AsNoTracking().Count(document => document.Id == row.Id)).ShouldBe(1);
            database.Read(context => context.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM cms_document WHERE id = {row.Id} FOR KEY SHARE""").Count()).ShouldBe(1);
            update = await RowLocks.BesideAsync(
                database.Database.ConnectionString,
                async () =>
                {
                    using var scope = database.Database.NewScope();
                    await scope.ServiceProvider.GetRequiredService<CmsDbContext>().Documents.Where(document => document.Id == row.Id)
                        .ExecuteUpdateAsync(set => set.SetProperty(document => document.DraftVersion, document => document.DraftVersion + 1), Token);
                },
                Token);
            update.IsCompleted.ShouldBeFalse();
        }

        await update.WaitAsync(RowLocks.Limit, Token);
        database.Document(id).DraftVersion.ShouldBe(row.DraftVersion + 1);
    }

    // A hold that waits past the lock timeout of its write fails with the error IsLockTimeout recognises, as the update did.
    [Fact]
    public async Task AHoldThatWaitsPastTheLockTimeoutIsALockTimeout()
    {
        Assert.SkipWhen(TestProvider.IsSqlite, TestProvider.ServerOnlyReason("the lock timeout of a row lock"));
        using var database = new ServiceDatabase();
        var row = database.Document(IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello" })));

        await using var holder = await Holder.BeginAsync(database);
        (await holder.HoldAsync(row.Id)).ShouldBeTrue();
        await using var waiter = await Holder.BeginAsync(database, TimeSpan.FromSeconds(1));
        var failed = await Should.ThrowAsync<Exception>(() => waiter.HoldAsync(row.Id).WaitAsync(RowLocks.Limit, Token));

        waiter.Provider.IsLockTimeout(failed).ShouldBeTrue(failed.ToString());
        waiter.Provider.IsDeadlock(failed).ShouldBeFalse();
    }

    // Two holds that cross: the database ends one as a deadlock's victim, which IsDeadlock recognises, so that the content
    // service runs it again, and the other goes on, since the database aborts the victim's transaction with it.
    [Fact]
    public async Task TwoHoldsThatCrossAreADeadlock()
    {
        Assert.SkipWhen(TestProvider.IsSqlite, TestProvider.ServerOnlyReason("the deadlock detection of row locks"));
        using var database = new ServiceDatabase();
        var first = database.Document(IdOf(await database.CreateAsync(Editor, new() { ["title"] = "One" })));
        var second = database.Document(IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Two" })));

        var one = await Holder.BeginAsync(database);
        var two = await Holder.BeginAsync(database);
        try
        {
            (await one.HoldAsync(first.Id)).ShouldBeTrue();
            (await two.HoldAsync(second.Id)).ShouldBeTrue();
            var oneWaits = await RowLocks.BesideAsync(database.Database.ConnectionString, () => one.HoldAsync(second.Id), Token);
            var twoWaits = two.HoldAsync(first.Id);

            var outcomes = await Task.WhenAll(OutcomeAsync(oneWaits), OutcomeAsync(twoWaits)).WaitAsync(RowLocks.Limit, Token);
            var failed = outcomes.Where(outcome => outcome is not null).ShouldHaveSingleItem()!;
            one.Provider.IsDeadlock(failed).ShouldBeTrue(failed.ToString());
            one.Provider.IsLockTimeout(failed).ShouldBeFalse();
        }
        finally
        {
            await one.DisposeAsync();
            await two.DisposeAsync();
        }
    }

    private static async Task<Exception?> OutcomeAsync(Task hold)
    {
        try
        {
            await hold;
            return null;
        }
#pragma warning disable CA1031 // The outcome is what the test asserts on.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return exception;
        }
    }
}
