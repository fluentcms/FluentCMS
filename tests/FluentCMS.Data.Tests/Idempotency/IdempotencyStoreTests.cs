using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Content;
using FluentCMS.Core.Idempotency;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Idempotency;

// The keys of #2933 on the run's database: the claim, the stored response, the refusals and the window.
public sealed class IdempotencyStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Principal Alice = new(PrincipalKind.AdminUser, Guid.CreateVersion7(), "alice");

    private static readonly Principal Bob = new(PrincipalKind.AdminUser, Guid.CreateVersion7(), "bob");

    private static readonly StoredResponse Created = new(201, new Dictionary<string, string> { ["Content-Type"] = "application/json; charset=utf-8", ["Location"] = "/x/1" }, """{"id":"1"}""");

    private readonly ManualTimeProvider _clock = new(Start);

    private readonly KeyGate _gate = new();

    private readonly KeyLogs _logs = new();

    private readonly AfterCommit _afterCommit = new();

    private readonly LostAnswer _lostAnswer = new();

    private readonly ServiceDatabase _database;

    public IdempotencyStoreTests() =>
        _database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton<TimeProvider>(_clock);
            cms.Services.AddSingleton<ILoggerProvider>(_logs);
            cms.Services.AddSingleton<IEntryChangeSink>(_afterCommit);
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(_gate, _lostAnswer));
            cms.Idempotency.Window = TimeSpan.FromHours(24);
            cms.Idempotency.InProgressTimeout = TimeSpan.FromMinutes(5);
        });

    public void Dispose() => _database.Dispose();

    private IdempotencyStore Store => _database.Database.Services.GetRequiredService<IdempotencyStore>();

    private Task<IdempotencyStart> BeginAsync(string key = "key-1", string hash = "hash-a", Principal? principal = null, SiteId? site = null) =>
        Store.BeginAsync(new OperationContext(principal ?? Alice, Origin.Api, site ?? SiteA, "en", null, "c", _clock.GetUtcNow()), key, hash, Token);

    private List<IdempotencyRow> Rows()
    {
        using var scope = _database.Database.NewScope();
        return [.. scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<IdempotencyRow>().AsNoTracking()];
    }

    private static async Task<string> RefusedAsync(Func<Task> work) =>
        (await Should.ThrowAsync<ValidationException>(work)).Errors.ShouldHaveSingleItem().Code;

    [Fact]
    public async Task AFirstRequestClaimsTheKeyAndARepeatGetsItsStoredResponse()
    {
        var first = await BeginAsync();
        first.Stored.ShouldBeNull();
        var claim = first.Claim.ShouldNotBeNull();

        await Store.CompleteAsync(claim, Created);
        var repeat = await BeginAsync();

        repeat.Claim.ShouldBeNull();
        var stored = repeat.Stored.ShouldNotBeNull();
        stored.Status.ShouldBe(201);
        stored.Headers.ShouldBe(Created.Headers);
        stored.Body.ShouldBe(Created.Body);
        var row = Rows().ShouldHaveSingleItem();
        row.KeyHash.Length.ShouldBe(64);
        row.KeyHash.ShouldNotContain("key-1");
        row.ExpiresAt.ShouldBe(Start.AddHours(24));
    }

    [Fact]
    public async Task TheSameKeyWithAnotherRequestIsRefusedWhetherTheFirstRunsOrAnswered()
    {
        var claim = (await BeginAsync()).Claim!;
        (await RefusedAsync(() => BeginAsync(hash: "hash-b"))).ShouldBe(IdempotencyErrorCodes.KeyReused);

        await Store.CompleteAsync(claim, Created);
        (await RefusedAsync(() => BeginAsync(hash: "hash-b"))).ShouldBe(IdempotencyErrorCodes.KeyReused);
    }

    [Fact]
    public async Task ARepeatWhileTheFirstRunsIsRefusedUntilItsLeaseRunsOutAndThenTakesItOver()
    {
        var first = (await BeginAsync()).Claim!;

        (await RefusedAsync(() => BeginAsync())).ShouldBe(IdempotencyErrorCodes.InProgress);

        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = (await BeginAsync()).Claim.ShouldNotBeNull();
        second.Id.ShouldBe(first.Id);
        second.Owner.ShouldNotBe(first.Owner);

        // The first request answers late: its response is not kept, the one that took over is.
        await Store.CompleteAsync(first, Created with { Body = "late" });
        Rows().ShouldHaveSingleItem().ResponseStatus.ShouldBeNull();
        await Store.CompleteAsync(second, Created);
        (await BeginAsync()).Stored!.Body.ShouldBe(Created.Body);
    }

    // #3307: while the first request runs its lease is renewed, so a repeat long after the lease would have run out is still
    // refused and nothing runs twice; once it stops, the lease runs out InProgressTimeout after the last renewal.
    [Fact]
    public async Task AHeldClaimIsRenewedSoARepeatPastTheLeaseIsStillRefused()
    {
        var claim = (await BeginAsync()).Claim!;
        var others = _clock.Timers;

        await using (Store.Hold(claim))
        {
            for (var turn = 0; turn < 6; turn++)
            {
                // The lease sets its next turn only once the renewal before has written, so the clock waits for it.
                await TurnSetAsync(others);
                _clock.Advance(TimeSpan.FromMinutes(5) / 3);
            }

            await TurnSetAsync(others);
            Rows().Single().LeaseUntil.ShouldBe(_clock.GetUtcNow() + TimeSpan.FromMinutes(5));
            (await RefusedAsync(() => BeginAsync())).ShouldBe(IdempotencyErrorCodes.InProgress);
        }

        _clock.Advance(TimeSpan.FromMinutes(4));
        (await RefusedAsync(() => BeginAsync())).ShouldBe(IdempotencyErrorCodes.InProgress);
        _clock.Advance(TimeSpan.FromMinutes(1));
        (await BeginAsync()).Claim.ShouldNotBeNull().Owner.ShouldNotBe(claim.Owner);
    }

    [Fact]
    public async Task AClaimThatAnsweredOrWasTakenOverIsNotRenewed()
    {
        var first = (await BeginAsync()).Claim!;
        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = (await BeginAsync()).Claim!;

        (await Store.RenewAsync(first, Token)).ShouldBe(IdempotencyRenewal.Lost);
        (await Store.RenewAsync(second, Token)).ShouldBe(IdempotencyRenewal.Renewed);

        await Store.CompleteAsync(second, Created);
        (await Store.RenewAsync(second, Token)).ShouldBe(IdempotencyRenewal.Lost);
    }

    // #3523: a key whose window ends while its request still holds the lease, as with a window shorter than the request,
    // is neither claimed afresh by a repeat nor removed by the retention job, so its renewals go on finding it.
    [Fact]
    public async Task AKeyPastItsWindowIsKeptWhileItsRequestHoldsTheLease()
    {
        var claim = (await BeginAsync()).Claim!;
        _clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        (await Store.RenewAsync(claim, Token)).ShouldBe(IdempotencyRenewal.Renewed);
        _clock.Advance(TimeSpan.FromMinutes(2));

        (await RefusedAsync(() => BeginAsync())).ShouldBe(IdempotencyErrorCodes.InProgress);
        (await RefusedAsync(() => BeginAsync(hash: "hash-b"))).ShouldBe(IdempotencyErrorCodes.InProgress);
        await RetainAsync();

        Rows().ShouldHaveSingleItem().Owner.ShouldBe(claim.Owner);
        (await Store.RenewAsync(claim, Token)).ShouldBe(IdempotencyRenewal.Renewed);

        // Once the request answered, the key past its window is claimed afresh.
        await Store.CompleteAsync(claim, Created);
        (await BeginAsync(hash: "hash-b")).Claim.ShouldNotBeNull().Id.ShouldNotBe(claim.Id);
    }

    // #3523: a repeat removes the expired row of a request whose lease ran out past the window, and the request renews
    // after the repeat's read found no row and before its insert (#3582): the request puts its claim back, the repeat's
    // insert collides with it, and the repeat, reading the key again, finds it held rather than run too.
    [Fact]
    public async Task ARepeatThatRemovedAnExpiredKeyLosesItToTheRequestThatPutsItBack()
    {
        var first = (await BeginAsync()).Claim!;
        _clock.Advance(TimeSpan.FromHours(24));
        (await Store.RenewAsync(first, Token)).ShouldBe(IdempotencyRenewal.Renewed);
        _clock.Advance(TimeSpan.FromMinutes(5));
        IdempotencyRenewal? renewal = null;
        var deleted = false;
        _gate.Arm(
            sql => (deleted |= TestProvider.IsDeleteFrom(sql, "cms_idempotency_key")) && (sql == KeyGate.BeginTransaction || TestProvider.IsInsertInto(sql, "cms_idempotency_key")),
            async () => renewal = await Store.RenewAsync(first, Token));

        (await RefusedAsync(() => BeginAsync())).ShouldBe(IdempotencyErrorCodes.InProgress);

        renewal.ShouldBe(IdempotencyRenewal.Restored);
        Rows().ShouldHaveSingleItem().Owner.ShouldBe(first.Owner);
    }

    // #3582: a repeat that took the key over inside its window, once the request's lease ran out, and answered under it, or
    // failed and freed it, leaves no row once its answer's window passed or it freed the key. The request cannot tell that
    // from a key that was only removed, so it does not put its claim back but is lost, rather than run its write again.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARequestWhoseKeyARepeatTookOverAndAnsweredOrFreedIsNotPutBack(bool answered)
    {
        var first = (await BeginAsync()).Claim!;
        _clock.Advance(TimeSpan.FromMinutes(5));
        var repeat = (await BeginAsync()).Claim.ShouldNotBeNull();
        if (answered)
        {
            await Store.CompleteAsync(repeat, Created);
            _clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(5));
            await RetainAsync();
        }
        else
        {
            await Store.ReleaseAsync(repeat);
        }

        Rows().ShouldBeEmpty();
        (await Store.RenewAsync(first, Token)).ShouldBe(IdempotencyRenewal.Lost);
        Rows().ShouldBeEmpty();
    }

    // #3582: nor is a claim whose lease ran out past the window put back a window or more later: a request that claimed the
    // key afresh since may have answered, and its row gone once its own window passed.
    [Fact]
    public async Task ARequestWhoseLeaseRanOutAWindowAgoIsNotPutBack()
    {
        var first = (await BeginAsync()).Claim!;
        _clock.Advance(TimeSpan.FromHours(24));
        (await Store.RenewAsync(first, Token)).ShouldBe(IdempotencyRenewal.Renewed);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await Store.CompleteAsync((await BeginAsync()).Claim.ShouldNotBeNull(), Created);
        _clock.Advance(TimeSpan.FromHours(24));
        await RetainAsync();

        Rows().ShouldBeEmpty();
        (await Store.RenewAsync(first, Token)).ShouldBe(IdempotencyRenewal.Lost);
    }

    // #3582: a put-back whose insert committed but reported a failure leaves the claim with the window of the row it wrote,
    // so that once a repeat took that row over, answered and the row went past its window, the request is not put back
    // again against the window of the row before.
    [Fact]
    public async Task APutBackThatCommittedButFailedKeepsTheWindowOfItsRow()
    {
        var first = (await BeginAsync()).Claim!;
        _clock.Advance(TimeSpan.FromHours(24));
        (await Store.RenewAsync(first, Token)).ShouldBe(IdempotencyRenewal.Renewed);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await RetainAsync();
        Rows().ShouldBeEmpty();

        _lostAnswer.Arm();
        await Should.ThrowAsync<InvalidOperationException>(() => Store.RenewAsync(first, Token));
        Rows().ShouldHaveSingleItem().Owner.ShouldBe(first.Owner);
        (await Store.RenewAsync(first, Token)).ShouldBe(IdempotencyRenewal.Renewed);

        _clock.Advance(TimeSpan.FromMinutes(5));
        await Store.CompleteAsync((await BeginAsync()).Claim.ShouldNotBeNull(), Created);
        _clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(2));
        await RetainAsync();
        Rows().ShouldBeEmpty();

        (await Store.RenewAsync(first, Token)).ShouldBe(IdempotencyRenewal.Lost);
        Rows().ShouldBeEmpty();
    }

    // #3523: a repeat that read a lapsed lease loses its takeover to a renewal of that lease before its update.
    [Fact]
    public async Task ATakeoverLosesToARenewalBetweenItsReadAndItsUpdate()
    {
        var first = (await BeginAsync()).Claim!;
        _clock.Advance(TimeSpan.FromMinutes(5));
        IdempotencyRenewal? renewal = null;
        _gate.Arm(sql => TestProvider.IsUpdateOf(sql, "cms_idempotency_key"), async () => renewal = await Store.RenewAsync(first, Token));

        (await RefusedAsync(() => BeginAsync())).ShouldBe(IdempotencyErrorCodes.InProgress);

        renewal.ShouldBe(IdempotencyRenewal.Renewed);
        Rows().ShouldHaveSingleItem().Owner.ShouldBe(first.Owner);
        _logs.Of(9140).ShouldBeEmpty();
    }

    // A claim a repeat took over, after renewals failed for a whole lease, is lost at the next turn, which cancels the
    // request's work rather than leave it running beside the repeat.
    [Fact]
    public async Task ALeaseFindsItsClaimTakenOverAndIsLost()
    {
        var claim = (await BeginAsync()).Claim!;
        var others = _clock.Timers;
        await using var lease = Store.Hold(claim);
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var heard = lease.Lost.Register(() => lost.TrySetResult());

        using (var scope = _database.Database.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<IdempotencyRow>()
                .Where(row => row.Id == claim.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Owner, Guid.NewGuid()), Token);
        }

        await TurnSetAsync(others);
        _clock.Advance(TimeSpan.FromMinutes(5) / 3);

        await lost.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        lease.Lost.IsCancellationRequested.ShouldBeTrue();
    }

    // The request whose claim is lost is cancelled: its work stops and it answers FCMS3142, rather than go on beside the
    // repeat. On SQLite, which has one writer, neither the renewal nor a repeat's takeover writes while the request's own
    // write transaction is open, so there the claim is lost only between its transactions.
    [Fact]
    public async Task ARequestWhoseClaimARepeatTookOverIsCancelledAndAnswersInProgress()
    {
        using var scope = _database.Database.NewScope();
        var http = Request(scope, "key-lost");
        var others = _clock.Timers;
        var working = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = IdempotentRequests.RunAsync(http, async () =>
        {
            working.TrySetResult();
            await Task.Delay(Timeout.Infinite, http.RequestAborted);
            return Results.Ok();
        });
        await working.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        await TakeOverAsync();
        await TurnSetAsync(others);
        _clock.Advance(TimeSpan.FromMinutes(5) / 3);

        var refused = (await Should.ThrowAsync<ValidationException>(() => running.WaitAsync(TimeSpan.FromSeconds(30), Token))).Errors.ShouldHaveSingleItem();
        refused.Code.ShouldBe(IdempotencyErrorCodes.InProgress);
        refused.Message.ShouldContain("rolled back");
    }

    // #3523: a request whose write committed before a repeat took its key, as when the key is lost while the content
    // service reads the entry back, which no cancellation stops, answers what it wrote rather than that it rolled back.
    // That answer is not kept, since the key is the repeat's.
    [Fact]
    public async Task ARequestWhoseKeyIsTakenOverAfterItsWriteCommittedAnswersTheWrite()
    {
        using var scope = _database.Database.NewScope();
        var http = Request(scope, "key-committed");
        var others = _clock.Timers;
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = IdempotentRequests.RunAsync(http, async () =>
        {
            using var heard = http.RequestAborted.Register(() => stopped.TrySetResult());
            committed.TrySetResult();
            await stopped.Task;
            return Results.Json(new { id = "1" }, statusCode: StatusCodes.Status201Created);
        });
        await committed.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        await TakeOverAsync();
        await TurnSetAsync(others);
        _clock.Advance(TimeSpan.FromMinutes(5) / 3);

        await AnswersCreatedAsync(http, await running.WaitAsync(TimeSpan.FromSeconds(30), Token));
        Rows().ShouldHaveSingleItem().ResponseStatus.ShouldBeNull();
    }

    // #3523: a key that was removed while its request ran, its lease out past its window, is not a key a repeat took: the
    // request puts its claim back and holds the key again, so a repeat meanwhile is refused, and its answer is kept and
    // replayed as usual.
    [Fact]
    public async Task ARequestWhoseKeyWasRemovedUnderItPutsItBackAndKeepsItsAnswer()
    {
        using var scope = _database.Database.NewScope();
        var http = Request(scope, "key-expired");
        var others = _clock.Timers;
        var working = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = IdempotentRequests.RunAsync(http, async () =>
        {
            working.TrySetResult();
            await release.Task.WaitAsync(http.RequestAborted);
            return Results.Json(new { id = "1" }, statusCode: StatusCodes.Status201Created);
        });
        await working.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var hash = Rows().Single().RequestHash;

        await HoldPastTheWindowAsync(others);
        await RemoveRowsAsync();
        _clock.Advance(TimeSpan.FromMinutes(5) / 3);
        await TurnSetAsync(others);

        _logs.Of(9146).ShouldHaveSingleItem();
        (await RefusedAsync(() => BeginAsync("key-expired", hash))).ShouldBe(IdempotencyErrorCodes.InProgress);
        release.TrySetResult();

        await AnswersCreatedAsync(http, await running.WaitAsync(TimeSpan.FromSeconds(30), Token));
        (await BeginAsync("key-expired", hash)).Stored.ShouldNotBeNull().Body.ShouldBe("""{"id":"1"}""");
    }

    // #3523: a repeat that claimed a removed key afresh before the request's renewal keeps it: the request, whose lease ran
    // out past the window so that it would put its claim back otherwise (#3582), is refused by the unique index and is
    // cancelled before its write, so only one of the two writes.
    [Fact]
    public async Task ARequestWhoseRemovedKeyARepeatClaimedIsCancelled()
    {
        using var scope = _database.Database.NewScope();
        var http = Request(scope, "key-reclaimed");
        var others = _clock.Timers;
        var working = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wrote = false;

        var running = IdempotentRequests.RunAsync(http, async () =>
        {
            working.TrySetResult();
            await Task.Delay(Timeout.Infinite, http.RequestAborted);
            wrote = true;
            return Results.Ok();
        });
        await working.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var hash = Rows().Single().RequestHash;

        await HoldPastTheWindowAsync(others);
        await RemoveRowsAsync();
        var repeat = (await BeginAsync("key-reclaimed", hash)).Claim.ShouldNotBeNull();
        _clock.Advance(TimeSpan.FromMinutes(5) / 3);

        (await RefusedAsync(() => running.WaitAsync(TimeSpan.FromSeconds(30), Token))).ShouldBe(IdempotencyErrorCodes.InProgress);
        wrote.ShouldBeFalse();
        Rows().ShouldHaveSingleItem().Owner.ShouldBe(repeat.Owner);
        _logs.Of(9145).ShouldHaveSingleItem();
        _logs.Of(9146).ShouldBeEmpty();
    }

    // #3485: a client that goes away once the work has answered, while its answer is captured, does not leave the key held
    // without an answer. The answer is captured all the same, since it is written to memory, and kept, so a retry with the
    // key is answered it, replayed, and the write runs once.
    [Fact]
    public async Task ARetryAfterTheClientWentAwayWhileTheAnswerWasWrittenGetsItAndRunsNothing()
    {
        var writes = 0;
        using var client = new CancellationTokenSource();
        using (var scope = _database.Database.NewScope())
        {
            var http = Request(scope, "key-aborted");
            http.RequestAborted = client.Token;
            await IdempotentRequests.RunAsync(http, () =>
            {
                writes++;
                return Task.FromResult<IResult>(new GoneWhileWritten(client, Results.Json(new { id = "1" }, statusCode: StatusCodes.Status201Created)));
            });
        }

        Rows().ShouldHaveSingleItem().ResponseStatus.ShouldBe(StatusCodes.Status201Created);
        using var retryScope = _database.Database.NewScope();
        var retry = Request(retryScope, "key-aborted");
        var answer = await IdempotentRequests.RunAsync(retry, () =>
        {
            writes++;
            return Task.FromResult(Results.Ok());
        });

        await answer.ExecuteAsync(retry);
        writes.ShouldBe(1);
        retry.Response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        retry.Response.Headers[IdempotentRequests.ReplayedHeader].ToString().ShouldBe("true");
        Encoding.UTF8.GetString(((MemoryStream)retry.Response.Body).ToArray()).ShouldBe("""{"id":"1"}""");
    }

    // #3485: the capture after the client went away is bounded by InProgressTimeout, so that an answer that never ends does
    // not hold the request: it stops, keeps nothing, and leaves the key held until its lease runs out, as before.
    [Fact]
    public async Task ACaptureAfterTheClientWentAwayStopsAtInProgressTimeout()
    {
        using var scope = _database.Database.NewScope();
        var http = Request(scope, "key-endless");
        using var client = new CancellationTokenSource();
        http.RequestAborted = client.Token;
        var others = _clock.Timers;
        var capturing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = IdempotentRequests.RunAsync(http, () => Task.FromResult<IResult>(new GoneWhileWritten(client, new Endless(capturing))));
        await capturing.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        await TurnSetAsync(others + 1);
        _clock.Advance(TimeSpan.FromMinutes(5));

        await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(30), Token));
        _logs.Of(9147).ShouldHaveSingleItem();
        Rows().ShouldHaveSingleItem().ResponseStatus.ShouldBeNull();
    }

    // #3485: a bulk whose client went away while it ran, before any of its writes committed, answers 200 with the rest of its
    // items not run, and does not throw. That answer is of work cut short, so it is not kept: the key stays held until its
    // lease runs out, and a retry then runs the bulk again, the items not run among them.
    [Fact]
    public async Task ABulkCutShortByItsClientBeforeACommitKeepsNothingAndARetryAfterTheLeaseRunsIt()
    {
        List<int> ran = [];
        using var client = new CancellationTokenSource();
        using (var scope = _database.Database.NewScope())
        {
            var http = Request(scope, "key-bulk");
            http.RequestAborted = client.Token;
            await Should.ThrowAsync<OperationCanceledException>(() => IdempotentRequests.RunAsync(http, () => BulkAsync(http, ran, () => ran.Count == 2 ? client.CancelAsync() : Task.CompletedTask)));
        }

        ran.ShouldBe([0, 1]);
        var row = Rows().ShouldHaveSingleItem();
        row.ResponseStatus.ShouldBeNull();
        (await RefusedAsync(() => BeginAsync("key-bulk", row.RequestHash))).ShouldBe(IdempotencyErrorCodes.InProgress);

        _clock.Advance(TimeSpan.FromMinutes(5));
        using var retryScope = _database.Database.NewScope();
        var retry = Request(retryScope, "key-bulk");
        await (await IdempotentRequests.RunAsync(retry, () => BulkAsync(retry, ran, () => Task.CompletedTask))).ExecuteAsync(retry);

        ran.ShouldBe([0, 1, 0, 1, 2, 3]);
        retry.Response.Headers[IdempotentRequests.ReplayedHeader].ShouldBeEmpty();
        Rows().ShouldHaveSingleItem().ResponseStatus.ShouldBe(StatusCodes.Status200OK);
    }

    // #3485: a create whose client went away before its commit throws on its token; nothing is kept, the key stays held until
    // its lease runs out, and a retry then runs the write.
    [Fact]
    public async Task ACreateStoppedByItsClientBeforeItsCommitKeepsNothingAndARetryAfterTheLeaseRunsIt()
    {
        var writes = 0;
        using var client = new CancellationTokenSource();
        using (var scope = _database.Database.NewScope())
        {
            var http = Request(scope, "key-before");
            http.RequestAborted = client.Token;
            await Should.ThrowAsync<OperationCanceledException>(() => IdempotentRequests.RunAsync(http, async () =>
            {
                await client.CancelAsync();
                http.RequestAborted.ThrowIfCancellationRequested();
                writes++;
                return Results.Json(new { id = "1" }, statusCode: StatusCodes.Status201Created);
            }));
        }

        writes.ShouldBe(0);
        Rows().ShouldHaveSingleItem().ResponseStatus.ShouldBeNull();

        _clock.Advance(TimeSpan.FromMinutes(5));
        using var retryScope = _database.Database.NewScope();
        var retry = Request(retryScope, "key-before");
        await AnswersCreatedAsync(retry, await IdempotentRequests.RunAsync(retry, () =>
        {
            writes++;
            return Task.FromResult(Results.Json(new { id = "1" }, statusCode: StatusCodes.Status201Created));
        }));
        writes.ShouldBe(1);
    }

    // #3590: a client that goes away once a create committed, while the content service reads the entry back, before the
    // handler returns, does not leave the key held without an answer: the answer of the entry is kept, so a retry with the
    // key is answered it, replayed, and the entry is written once.
    [Fact]
    public async Task ARetryAfterTheClientWentAwayOnceACreateCommittedGetsTheEntryAndWritesNothing()
    {
        var runs = 0;
        using var client = new CancellationTokenSource();
        _afterCommit.Then = _ => client.CancelAsync();
        using (var scope = _database.Database.NewScope())
        {
            var http = ContentRequest(scope, "key-create", client.Token);
            await IdempotentRequests.RunAsync(http, async () =>
            {
                runs++;
                var created = await Content(scope).CreateAsync("author", new ContentDocument { ["name"] = "Ada" }, http.RequestAborted);
                client.IsCancellationRequested.ShouldBeTrue();
                return Results.Json(new { id = IdOf(created) }, statusCode: StatusCodes.Status201Created);
            });
        }

        var row = Rows().ShouldHaveSingleItem();
        row.ResponseStatus.ShouldBe(StatusCodes.Status201Created);
        var id = Authors().ShouldHaveSingleItem();
        row.ResponseBody.ShouldBe($$"""{"id":"{{id}}"}""");

        _afterCommit.Then = null;
        using var retryScope = _database.Database.NewScope();
        var retry = ContentRequest(retryScope, "key-create", CancellationToken.None);
        await ReplaysAsync(retry, row.ResponseBody!, StatusCodes.Status201Created, async () =>
        {
            runs++;
            return Results.Json(await Content(retryScope).CreateAsync("author", new ContentDocument { ["name"] = "Ada" }, retry.RequestAborted));
        });

        runs.ShouldBe(1);
        Authors().ShouldHaveSingleItem().ShouldBe(id);
    }

    // #3590: a bulk whose client went away once its first item committed runs its other items, within InProgressTimeout,
    // and its whole answer is kept, so a retry with the key is answered it, replayed, and runs nothing.
    [Fact]
    public async Task ABulkWhoseClientWentAwayAfterItsFirstCommitRunsTheRestAndARetryGetsItsAnswer()
    {
        using var client = new CancellationTokenSource();
        _afterCommit.Then = _ => client.CancelAsync();
        using (var scope = _database.Database.NewScope())
        {
            var http = ContentRequest(scope, "key-bulk-committed", client.Token);
            await IdempotentRequests.RunAsync(http, () => ContentBulkAsync(http, scope, "One", "Two", "Three"));
        }

        Authors().Count.ShouldBe(3);
        var row = Rows().ShouldHaveSingleItem();
        row.ResponseStatus.ShouldBe(StatusCodes.Status200OK);
        row.ResponseBody.ShouldBe("""{"items":["Succeeded","Succeeded","Succeeded"],"cancelled":false}""");

        _afterCommit.Then = null;
        await RetryBulkAsync("key-bulk-committed", row.ResponseBody!);
        Authors().Count.ShouldBe(3);
    }

    // #3590: a bulk that its client left after its first commit, and that is still running when InProgressTimeout has passed
    // since, stops at its next item and answers the rest as not run; that answer is kept, so a retry is told exactly what ran.
    [Fact]
    public async Task ABulkThatRunsPastTheBoundAfterItsClientWentAwayKeepsWhatRanAndTheRestAsNotRun()
    {
        using var client = new CancellationTokenSource();
        var commits = 0;
        _afterCommit.Then = async _ =>
        {
            if (++commits == 1)
            {
                await client.CancelAsync();
            }
            else
            {
                _clock.Advance(TimeSpan.FromMinutes(5));
            }
        };
        using (var scope = _database.Database.NewScope())
        {
            var http = ContentRequest(scope, "key-bulk-bound", client.Token);
            await IdempotentRequests.RunAsync(http, () => ContentBulkAsync(http, scope, "One", "Two", "Three"));
        }

        Authors().Count.ShouldBe(2);
        var row = Rows().ShouldHaveSingleItem();
        row.ResponseStatus.ShouldBe(StatusCodes.Status200OK);
        row.ResponseBody.ShouldBe("""{"items":["Succeeded","Succeeded","NotRun"],"cancelled":true}""");

        _afterCommit.Then = null;
        await RetryBulkAsync("key-bulk-bound", row.ResponseBody!);
        Authors().Count.ShouldBe(2);
    }

    private async Task RetryBulkAsync(string key, string body)
    {
        using var scope = _database.Database.NewScope();
        var retry = ContentRequest(scope, key, CancellationToken.None);
        await ReplaysAsync(retry, body, StatusCodes.Status200OK, () => ContentBulkAsync(retry, scope, "One", "Two", "Three"));
    }

    private static async Task ReplaysAsync(HttpContext http, string body, int status, Func<Task<IResult>> handler)
    {
        await (await IdempotentRequests.RunAsync(http, handler)).ExecuteAsync(http);
        http.Response.StatusCode.ShouldBe(status);
        http.Response.Headers[IdempotentRequests.ReplayedHeader].ToString().ShouldBe("true");
        Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray()).ShouldBe(body);
    }

    private static async Task<IResult> ContentBulkAsync(HttpContext http, IServiceScope scope, params string[] names)
    {
        var request = new BulkRequest { Operation = BulkOperation.Create, Entries = [.. names.Select(name => new ContentDocument { ["name"] = name })] };
        var result = await Content(scope).BulkAsync("author", request, http.RequestAborted);
        return Results.Json(new { items = result.Items.Select(item => item.Status.ToString()), cancelled = result.Cancelled });
    }

    private static IContentService Content(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IContentService>();

    // A keyed request of the content routes, whose operation the content service reads, as the entry point sets it.
    private DefaultHttpContext ContentRequest(IServiceScope scope, string key, CancellationToken client)
    {
        var http = Request(scope, key);
        http.RequestAborted = client;
        scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>().Current =
            new OperationContext(Editor, Origin.Api, SiteA, "en", "client", "c", _clock.GetUtcNow());
        return http;
    }

    private List<Guid> Authors() => _database.Read(context => context.Documents.Where(document => document.Type == "author").Select(document => document.DocumentId).Distinct().ToList());

    // Four items, as ContentService.RunBulkAsync runs them: an item after the token was cancelled is not run, and the answer
    // is 200 with every item, whether or not all ran.
    private static async Task<IResult> BulkAsync(HttpContext http, List<int> ran, Func<Task> afterEach)
    {
        List<object> results = [];
        for (var index = 0; index < 4; index++)
        {
            if (http.RequestAborted.IsCancellationRequested)
            {
                results.Add(new { index, status = "notRun" });
                continue;
            }

            ran.Add(index);
            results.Add(new { index, status = "succeeded" });
            await afterEach();
        }

        return Results.Json(new { items = results, cancelled = http.RequestAborted.IsCancellationRequested });
    }

    // Told of each write the content service committed, before it reads the entry back.
    private sealed class AfterCommit : IEntryChangeSink
    {
        public Func<EntryChange, Task>? Then { get; set; }

        public async ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            if (Then is { } then)
            {
                await then(change);
            }
        }
    }

    // An answer whose client goes away as it begins to be written, once the work has answered.
    private sealed class GoneWhileWritten(CancellationTokenSource client, IResult answer) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            await client.CancelAsync();
            await answer.ExecuteAsync(httpContext);
        }
    }

    // An answer that writes until the request's token stops it.
    private sealed class Endless(TaskCompletionSource capturing) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            capturing.TrySetResult();
            await Task.Delay(Timeout.Infinite, httpContext.RequestAborted);
        }
    }

    private DefaultHttpContext Request(IServiceScope scope, string key)
    {
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = HttpMethods.Post;
        http.Request.Path = "/api/management/v1/content/article";
        http.Request.ContentType = "application/json";
        http.Request.Body = new MemoryStream("{}"u8.ToArray());
        http.Request.Headers[IdempotencyStore.Header] = key;
        http.Response.Body = new MemoryStream();
        http.Features.Set(new ApiRequest(new OperationContext(Alice, Origin.Api, SiteA, "en", null, "c", _clock.GetUtcNow()), "en"));
        return http;
    }

    private static async Task AnswersCreatedAsync(HttpContext http, IResult answer)
    {
        await answer.ExecuteAsync(http);
        http.Response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        http.Response.Headers[IdempotentRequests.ReplayedHeader].ShouldBeEmpty();
        Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray()).ShouldBe("""{"id":"1"}""");
    }

    // As a repeat that took the key over does, after the renewals failed for a whole lease.
    private async Task TakeOverAsync()
    {
        using var scope = _database.Database.NewScope();
        (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<IdempotencyRow>()
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Owner, Guid.NewGuid()), Token)).ShouldBe(1);
    }

    // As the retention job or a repeat does once the key's window and lease have both run out.
    private async Task RemoveRowsAsync()
    {
        using var scope = _database.Database.NewScope();
        (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<IdempotencyRow>().ExecuteDeleteAsync(Token)).ShouldBe(1);
    }

    private async Task RetainAsync()
    {
        using var scope = _database.Database.NewScope();
        var operation = new OperationContext(Principal.System, Origin.Job, SiteA, "en", null, "retention", _clock.GetUtcNow());
        await scope.ServiceProvider.GetRequiredService<IdempotencyRetentionJob>().ExecuteAsync(
            new IdempotencyRetentionPayload(),
            new JobContext(Guid.CreateVersion7(), IdempotencyRetentionJob.Type, "default", 1, 3, operation, Token));
    }

    private Task TurnSetAsync(int others) => _clock.WhenTimersAsync(others + 1).WaitAsync(TimeSpan.FromSeconds(30), Token);

    // Renews the lease of a running request until it ends past the key's window, as for a request that outlives it. The
    // jump to the window's end renews at a time the jump does not fix; the turn after it renews at the window's end or
    // later, so the lease it writes ends past the window.
    private async Task HoldPastTheWindowAsync(int others)
    {
        await TurnSetAsync(others);
        _clock.Advance(TimeSpan.FromHours(24));
        await TurnSetAsync(others);
        _clock.Advance(TimeSpan.FromMinutes(5) / 3);
        await TurnSetAsync(others);
    }

    [Fact]
    public async Task OfManyRequestsThatSendOneKeyAtOnceOneRunsAndTheOthersAreRefused()
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            try
            {
                return (await BeginAsync()).Claim is null ? "stored" : "claimed";
            }
            catch (ValidationException exception)
            {
                return exception.Errors.Single().Code;
            }
        }, Token)));

        attempts.Count(outcome => outcome == "claimed").ShouldBe(1);
        attempts.Where(outcome => outcome != "claimed").ShouldAllBe(outcome => outcome == IdempotencyErrorCodes.InProgress);
        Rows().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task TwoPrincipalsAndTwoSitesNeverShareAKey()
    {
        await Store.CompleteAsync((await BeginAsync()).Claim!, Created);

        (await BeginAsync(principal: Bob, hash: "hash-b")).Claim.ShouldNotBeNull();
        (await BeginAsync(site: SiteB, hash: "hash-c")).Claim.ShouldNotBeNull();
        (await BeginAsync(principal: new Principal(PrincipalKind.ApiKey, Alice.Id, "key"), hash: "hash-d")).Claim.ShouldNotBeNull();
        Rows().Count.ShouldBe(4);
    }

    [Fact]
    public async Task AKeyIsComparedExactly()
    {
        await Store.CompleteAsync((await BeginAsync("Key-A")).Claim!, Created);

        (await BeginAsync("key-a", hash: "hash-b")).Claim.ShouldNotBeNull();
    }

    [Fact]
    public async Task AReleasedKeyRunsAgain()
    {
        await Store.ReleaseAsync((await BeginAsync()).Claim!);

        Rows().ShouldBeEmpty();
        (await BeginAsync(hash: "hash-b")).Claim.ShouldNotBeNull();
    }

    [Fact]
    public async Task AKeyPastItsWindowIsClaimedAfresh()
    {
        await Store.CompleteAsync((await BeginAsync()).Claim!, Created);

        _clock.Advance(TimeSpan.FromHours(24));
        var again = await BeginAsync(hash: "hash-b");

        again.Claim.ShouldNotBeNull();
        var row = Rows().ShouldHaveSingleItem();
        row.RequestHash.ShouldBe("hash-b");
        row.ResponseStatus.ShouldBeNull();
    }

    [Fact]
    public async Task TheRetentionJobRemovesTheKeysPastTheirWindowOnly()
    {
        await Store.CompleteAsync((await BeginAsync("old")).Claim!, Created);
        await BeginAsync("old-running");
        _clock.Advance(TimeSpan.FromHours(23));
        await BeginAsync("recent");
        _clock.Advance(TimeSpan.FromHours(1));

        await RetainAsync();

        Rows().ShouldHaveSingleItem().ExpiresAt.ShouldBe(Start.AddHours(47));
    }

    [Fact]
    public async Task TheRetentionScheduleIsStoredAtStartup()
    {
        await _database.Database.Services.GetServices<IHostedService>().OfType<IdempotencyRetentionScheduleService>().Single().StartAsync(Token);

        using var scope = _database.Database.NewScope();
        var job = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().AsNoTracking().Single(row => row.Name == IdempotencyRetentionJob.RecurringName);
        job.Type.ShouldBe(IdempotencyRetentionJob.Type);
        job.Cron.ShouldBe(IdempotencyRetentionJob.Schedule);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" padded")]
    [InlineData("tab\there")]
    [InlineData("café")]
    public void AKeyThatIsNotVisibleAsciiIsRefused(string key) =>
        Should.Throw<ValidationException>(() => IdempotencyStore.Key([key])).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdempotencyErrorCodes.InvalidKey);

    [Fact]
    public void AKeyIsOneValueOfAtMost255Characters()
    {
        IdempotencyStore.Key([new string('k', 255)]).Length.ShouldBe(255);
        IdempotencyStore.Key(["550e8400-e29b-41d4-a716-446655440000"]).ShouldBe("550e8400-e29b-41d4-a716-446655440000");
        Should.Throw<ValidationException>(() => IdempotencyStore.Key([new string('k', 256)]));
        Should.Throw<ValidationException>(() => IdempotencyStore.Key(["a", "b"])).Errors.ShouldHaveSingleItem().Path.ShouldBe("Idempotency-Key");
    }

    // Runs what a test gives it before the first command of the store that matches once armed, or before the first
    // transaction it begins, as BeginTransaction, and lets the command or the transaction go on when that is done.
    private sealed class KeyGate : DbCommandInterceptor, IDbTransactionInterceptor
    {
        public const string BeginTransaction = "BEGIN TRANSACTION";

        private Func<string, bool>? _matches;
        private Func<Task>? _at;

        public void Arm(Func<string, bool> matches, Func<Task> at)
        {
            _at = at;
            Volatile.Write(ref _matches, matches);
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PassAsync(command.CommandText);
            return result;
        }

        // A save sends its batch as a query on some providers.
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PassAsync(command.CommandText);
            return result;
        }

        // A save may begin a transaction before its insert, which on SQLite holds the writer until it ends.
        public async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            await PassAsync(BeginTransaction);
            return result;
        }

        private async Task PassAsync(string sql)
        {
            if (Volatile.Read(ref _matches) is { } matches && matches(sql) && Interlocked.CompareExchange(ref _matches, null, matches) == matches)
            {
                await _at!().WaitAsync(TimeSpan.FromSeconds(30), Token);
            }
        }
    }

    // Once armed, fails the next save of the store after it committed, as a connection lost before its answer would.
    private sealed class LostAnswer : SaveChangesInterceptor
    {
        private int _armed;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _armed, 0) == 1 ? throw new InvalidOperationException("The answer of a save that committed was lost.") : ValueTask.FromResult(result);
    }

    // What the store logs, by event id.
    private sealed class KeyLogs : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<(int Id, string Message)> _logged = new();

        public List<string> Of(int id) => [.. _logged.Where(entry => entry.Id == id).Select(entry => entry.Message)];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _logged.Enqueue((eventId.Id, formatter(state, exception)));

        public void Dispose()
        {
        }
    }
}
