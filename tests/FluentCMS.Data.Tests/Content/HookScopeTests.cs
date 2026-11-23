using System.Collections.Concurrent;
using System.Data.Common;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Hooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// #3307: a scope that left a hook running, at its timeout or with its cancelled caller, saves and commits nothing more. The
// hooks' timeout runs on a clock of the test's, so nothing here waits for time to pass.
public sealed class HookScopeTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan HookTimeout = new ContentWriteOptions().HookTimeout;

    private static readonly DateTimeOffset Start = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private const string InsideTag = "the left hook's read";

    private readonly ManualTimeProvider _clock = new(Start);
    private readonly Script _script = new();
    private readonly CancellationTokenSource _caller = new();
    private readonly HookLogs _logs = new();
    private readonly ServiceDatabase _database;

    public HookScopeTests() =>
        _database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton<TimeProvider>(_clock);
            cms.Services.AddSingleton(_script);
            cms.Services.AddSingleton<ILoggerProvider>(_logs);
            cms.Services.AddScoped<IBeforeCreateHook<ContentDocument>, LeftHook>();
            cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, NestingHook>();
            cms.Services.AddScoped<IAfterCreateHook<ContentDocument>, FollowingHook>();
            cms.Services.ConfigureDbContext<CmsDbContext>((services, options) =>
                options.AddInterceptors(new MarksAsItSaves(_script, services.GetRequiredService<HookScopeGuard>()), new HeldInside(_script)));
        });

    public void Dispose()
    {
        _script.Release.TrySetResult();
        _caller.Dispose();
        _database.Dispose();
    }

    // The outer write's hook catches what its nested write failed with and answers as if nothing had happened.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWriteThatSwallowsTheHookLeftInAWriteNestedInItIsRefusedWholeAndRunsNoMoreHooks(bool callerCancels)
    {
        var shorter = TimeSpan.FromSeconds(1);
        _script.Nested = async services =>
        {
            // The nested hook's time runs out first, while the outer hook still has time left.
            services.GetRequiredService<ContentWriteOptions>().HookTimeout = shorter;
            _script.Failure = await FailureOfAsync(() => services.GetRequiredService<IContentService>().CreateAsync("article", Titled("Left"), _caller.Token));
        };
        var before = State();

        var creating = _database.CreateAsync(Editor, Titled("Outer"));
        await LeaveAsync(callerCancels, shorter);

        var refused = await Should.ThrowAsync<HookRejectedException>(() => creating.WaitAsync(Bound, Token));
        refused.Code.ShouldBe("FCMS2410");
        refused.Message.ShouldContain(typeof(LeftHook).FullName!);
        ShouldBeHowItWasLeft(_script.Failure, callerCancels);
        _script.FollowingRan.ShouldBeFalse();
        State().ShouldBe(before);
    }

    // No hook runs after the nested write here, so it is the commit that refuses.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATransactionOfTheServiceDoesNotCommitOnceAHookInItWasLeft(bool callerCancels)
    {
        var before = State();

        var writing = _database.AsAsync(Editor, services => WriteAroundAsync(services.GetRequiredService<ContentService>()));
        await LeaveAsync(callerCancels);

        (await Should.ThrowAsync<HookRejectedException>(() => writing.WaitAsync(Bound, Token))).Code.ShouldBe("FCMS2410");
        State().ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASavepointIsRolledBackRatherThanReleasedOnceAHookInItWasLeftAndTheCallersCommitIsRefused(bool callerCancels)
    {
        var before = State();

        await _database.AsAsync(Editor, async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            await using (var transaction = await context.Database.BeginTransactionAsync(Token))
            {
                var writing = WriteAroundAsync(services.GetRequiredService<ContentService>());
                await LeaveAsync(callerCancels);

                (await Should.ThrowAsync<HookRejectedException>(() => writing.WaitAsync(Bound, Token))).Code.ShouldBe("FCMS2410");

                // A read is still answered, and finds the savepoint rolled back with the write that had succeeded in it.
                (await context.Documents.CountAsync(Token)).ShouldBe(0);
                (await Should.ThrowAsync<HookRejectedException>(() => transaction.CommitAsync(Token))).Code.ShouldBe("FCMS2410");
            }

            return true;
        });

        State().ShouldBe(before);
    }

    // With a transaction of its own and in the caller's, so on the savepoint's path too.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AWriteThatLeavesAHookTakesWhatItTrackedOffTheContextAndKeepsWhatTheCallerTracked(bool inCallersTransaction, bool callerCancels)
    {
        _script.BeforeWaiting = services => services.GetRequiredService<CmsDbContext>().Add(new Role { Name = "The hook's" });

        await _database.AsAsync(Editor, async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            context.Add(new Role { Name = "The caller's" });
            var transaction = inCallersTransaction ? await context.Database.BeginTransactionAsync(Token) : null;

            var creating = services.GetRequiredService<IContentService>().CreateAsync("article", Titled("Left"), _caller.Token);
            await LeaveAsync(callerCancels);
            ShouldBeHowItWasLeft(await FailureOfAsync(() => creating.WaitAsync(Bound, Token)), callerCancels);

            context.ChangeTracker.Entries<Role>().Select(entry => (entry.Entity.Name, entry.State)).ShouldBe([("The caller's", EntityState.Added)]);
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            return true;
        });
    }

    [Fact]
    public async Task ACancelledCallerThatLeavesARunningHookEndsTheWritesOfItsScope()
    {
        _script.Late = services => SaveARoleAsync(services.GetRequiredService<CmsDbContext>());
        var before = State();

        await _database.AsAsync(Editor, async services =>
        {
            var content = services.GetRequiredService<IContentService>();
            var creating = content.CreateAsync("article", Titled("Left"), _caller.Token);
            await LeaveAsync(callerCancels: true);
            await Should.ThrowAsync<OperationCanceledException>(() => creating.WaitAsync(Bound, Token));

            (await Should.ThrowAsync<HookRejectedException>(() => content.CreateAsync("article", Titled("Next"), Token))).Code.ShouldBe("FCMS2410");
            await ReleaseAsync();
            return true;
        });

        _script.Failure.ShouldBeOfType<HookRejectedException>().Code.ShouldBe("FCMS2410");
        State().ShouldBe(before);
    }

    // #3542: a hook left by its cancelled caller is logged by name, as one that timed out is, each with its own event.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AHookThatWasLeftIsLoggedByName(bool callerCancels)
    {
        var creating = _database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().CreateAsync("article", Titled("Left"), _caller.Token));
        await LeaveAsync(callerCancels);
        ShouldBeHowItWasLeft(await FailureOfAsync(() => creating.WaitAsync(Bound, Token)), callerCancels);

        var logged = _logs.Of(callerCancels ? 9408 : 9406).ShouldHaveSingleItem();
        logged.ShouldContain(typeof(LeftHook).FullName!);
        logged.ShouldContain(nameof(HookKind.BeforeCreate));
        _logs.Of(callerCancels ? 9406 : 9408).ShouldBeEmpty();
    }

    // #3542: the write's rollback and then the disposal of its scope run while the hook its cancelled caller left is inside
    // a command on the same connection, its reader open. Both end, the write fails with the cancellation, nothing commits,
    // and the hook's command ends once it goes on.
    [Fact]
    public async Task AWriteRollsBackAndItsScopeEndsWhileTheHookItLeftIsInsideACommand()
    {
        _script.Inside = services => services.GetRequiredService<CmsDbContext>().Documents.IgnoreQueryFilters().TagWith(InsideTag).CountAsync(Token);
        var before = State();

        var failure = await _database.AsAsync(Editor, async services =>
        {
            var creating = services.GetRequiredService<IContentService>().CreateAsync("article", Titled("Left"), _caller.Token);
            await LeaveAsync(callerCancels: true);
            return await FailureOfAsync(() => creating.WaitAsync(Bound, Token));
        }).WaitAsync(Bound, Token);

        failure.ShouldBeAssignableTo<OperationCanceledException>();
        _script.Ended.Task.IsCompleted.ShouldBeFalse();
        await ReleaseAsync();
        State().ShouldBe(before);

        // The database's writer and the rows' locks are free again.
        await _database.CreateAsync(Editor, Titled("After")).WaitAsync(Bound, Token);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("save synchronously")]
    [InlineData("update")]
    [InlineData("update synchronously")]
    [InlineData("delete")]
    [InlineData("sql")]
    [InlineData("stage")]
    [InlineData("commit")]
    public async Task WhatAHookSavesOnTheContextAfterItsTimeoutIsRefused(string how)
    {
        await _database.CreateAsync(Editor, Titled("Kept"));
        _script.Late = services => WriteLateAsync(services.GetRequiredService<CmsDbContext>(), how);
        var before = State();

        await _database.AsAsync(Editor, async services =>
        {
            var creating = services.GetRequiredService<IContentService>().CreateAsync("article", Titled("Left"), Token);
            await LeaveAsync(callerCancels: false);
            await Should.ThrowAsync<HookRejectedException>(() => creating.WaitAsync(Bound, Token));
            await ReleaseAsync();
            return true;
        });

        _script.Failure.ShouldBeOfType<HookRejectedException>().Code.ShouldBe("FCMS2410");
        State().ShouldBe(before);
    }

    // The outer transaction is gone by the time the nested write goes on, so what it sends would commit on its own.
    [Theory]
    [InlineData("save")]
    [InlineData("update")]
    public async Task AWriteNestedInAHookThatTimedOutDoesNotCommitOnItsOwnAfterTheOuterTransactionEnded(string how)
    {
        await _database.CreateAsync(Editor, Titled("Kept"));
        _script.Nested = async services =>
        {
            var service = services.GetRequiredService<ContentService>();
            var context = services.GetRequiredService<CmsDbContext>();
            _script.Failure = await FailureOfAsync(() => service.InTransactionAsync(
                async () =>
                {
                    _script.Entered.TrySetResult();
                    await _script.Release.Task;
                    await WriteLateAsync(context, how);
                    return true;
                },
                Token));
        };
        var before = State();

        await _database.AsAsync(Editor, async services =>
        {
            var creating = services.GetRequiredService<IContentService>().CreateAsync("article", Titled("Outer"), Token);
            await LeaveAsync(callerCancels: false);
            await Should.ThrowAsync<HookRejectedException>(() => creating.WaitAsync(Bound, Token));
            await ReleaseAsync();
            return true;
        });

        _script.Failure.ShouldNotBeNull();
        State().ShouldBe(before);
    }

    // A save asks the guard when it begins and again as it sends, since the mark can come between the two.
    [Fact]
    public async Task ASaveThatBeganBeforeTheScopeWasMarkedDoesNotCommit()
    {
        var before = State();
        _script.MarkAsItSaves = true;

        var failed = await _database.AsAsync(Editor, services => FailureOfAsync(() => SaveARoleAsync(services.GetRequiredService<CmsDbContext>())));

        failed.ShouldNotBeNull().GetBaseException().ShouldBeOfType<HookRejectedException>().Code.ShouldBe("FCMS2410");
        State().ShouldBe(before);
    }

    // The last attempt still records its failure, which the marked scope could not save.
    [Fact]
    public async Task AScheduledPublishWhoseHookTimedOutOnItsLastAttemptIsRecordedAsFailed()
    {
        var script = new Script();
        using var fixture = new SchedulingFixture(more: cms =>
        {
            cms.Services.AddSingleton(script);
            cms.Services.AddScoped<IBeforePublishHook<ContentDocument>, LeftPublishHook>();
        });
        var user = fixture.Publisher();
        var id = await fixture.DraftAsync();
        await fixture.ScheduleAsync(user, id, SchedulingFixture.At(SchedulingFixture.Start.AddHours(1)));
        var row = fixture.Schedules(id).Single();

        var running = fixture.RunAsync(row.Id, row.JobId!.Value, user, lastAttempt: true);
        await script.Entered.Task.WaitAsync(Bound, Token);
        fixture.Clock.Advance(HookTimeout);
        (await Should.ThrowAsync<HookRejectedException>(() => running.WaitAsync(Bound, Token))).Code.ShouldBe("FCMS2410");
        script.Release.TrySetResult();

        var ended = fixture.Schedules(id).Single();
        ended.Status.ShouldBe(ScheduleRow.Failed);
        ended.Error.ShouldNotBeNull().ShouldContain("FCMS2410");
        fixture.Database.Document(id).SchedulePublishAt.ShouldBeNull();
    }

    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    // A write that succeeds, then one that leaves a hook, whose failure the work swallows.
    private Task<bool> WriteAroundAsync(ContentService service) => service.InTransactionAsync(
        async () =>
        {
            await service.CreateAsync("article", Titled("Kept"), Token);
            await FailureOfAsync(() => service.CreateAsync("article", Titled("Left"), _caller.Token));
            return true;
        },
        Token);

    private async Task LeaveAsync(bool callerCancels, TimeSpan? timeout = null)
    {
        await _script.Entered.Task.WaitAsync(Bound, Token);
        if (callerCancels)
        {
            await _caller.CancelAsync();
        }
        else
        {
            _clock.Advance(timeout ?? HookTimeout);
        }
    }

    private async Task ReleaseAsync()
    {
        _script.Release.TrySetResult();
        await _script.Ended.Task.WaitAsync(Bound, Token);
    }

    private static void ShouldBeHowItWasLeft(Exception? failure, bool callerCancels)
    {
        if (callerCancels)
        {
            failure.ShouldBeAssignableTo<OperationCanceledException>();
        }
        else
        {
            failure.ShouldBeOfType<HookRejectedException>().Code.ShouldBe("FCMS2410");
        }
    }

    private static async Task<Exception?> FailureOfAsync(Func<Task> work)
    {
        try
        {
            await work();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Task<int> SaveARoleAsync(CmsDbContext context)
    {
        context.Add(new Role { Name = "Late" });
        return context.SaveChangesAsync(Token);
    }

    private static async Task WriteLateAsync(CmsDbContext context, string how)
    {
        switch (how)
        {
            case "save":
                await SaveARoleAsync(context);
                break;
            case "save synchronously":
                context.Add(new Role { Name = "Late" });
#pragma warning disable CA1849, xUnit1051 // The synchronous path is the one under test.
                context.SaveChanges();
                break;
            case "update synchronously":
                context.Documents.ExecuteUpdate(set => set.SetProperty(document => document.DraftVersion, 99));
#pragma warning restore CA1849, xUnit1051
                break;
            case "update":
                await context.Documents.ExecuteUpdateAsync(set => set.SetProperty(document => document.DraftVersion, 99), Token);
                break;
            case "delete":
                await context.Versions.ExecuteDeleteAsync(Token);
                break;
            case "sql":
                await context.Database.ExecuteSqlRawAsync("UPDATE cms_document SET draft_version = 99", Token);
                break;
            case "stage":
                var now = Start;
                await OutboxStaging.SaveAsync(
                    context,
                    [new OutboxMessage { Id = Guid.CreateVersion7(), EventId = Guid.CreateVersion7(), EventType = "late", Handler = "late", Payload = "{}", OccurredAt = now, AvailableAt = now }],
                    Token);
                break;
            case "commit":
                await using (var transaction = await context.Database.BeginTransactionAsync(Token))
                {
                    await transaction.CommitAsync(Token);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(how), how, null);
        }
    }

    private string State() => _database.Read(context => string.Join(
        ';',
        _database.Footprint(),
        context.Set<Role>().Count(),
        context.Set<OutboxMessage>().Count(),
        string.Join(',', context.Documents.IgnoreQueryFilters().Select(document => document.DraftVersion).ToList())));

    private sealed class Script
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Action<IServiceProvider>? BeforeWaiting { get; set; }

        public Func<IServiceProvider, Task>? Late { get; set; }

        public Func<IServiceProvider, Task>? Inside { get; set; }

        public Func<IServiceProvider, Task>? Nested { get; set; }

        public Exception? Failure { get; set; }

        public bool FollowingRan { get; set; }

        public bool MarkAsItSaves { get; set; }
    }

    // Ignores its token, and does what the test says once it is let go.
    private sealed class LeftHook(Script script, IServiceProvider services) : IBeforeCreateHook<ContentDocument>
    {
        public async ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (!Equals(context.Entry["title"], "Left"))
            {
                return;
            }

            script.BeforeWaiting?.Invoke(services);
            if (script.Inside is { } inside)
            {
                // HeldInside enters and holds it in the command.
                script.Failure = await FailureOfAsync(() => inside(services));
                script.Ended.TrySetResult();
                return;
            }

            script.Entered.TrySetResult();
            await script.Release.Task;
            if (script.Late is { } late)
            {
                script.Failure = await FailureOfAsync(() => late(services));
            }

            script.Ended.TrySetResult();
        }
    }

    private sealed class NestingHook(Script script, IServiceProvider services) : IAfterCreateHook<ContentDocument>
    {
        public async ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (!Equals(context.Entry["title"], "Outer"))
            {
                return;
            }

            await script.Nested!(services);
            script.Ended.TrySetResult();
        }
    }

    private sealed class FollowingHook(Script script) : IAfterCreateHook<ContentDocument>
    {
        public ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            script.FollowingRan |= Equals(context.Entry["title"], "Outer");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LeftPublishHook(Script script) : IBeforePublishHook<ContentDocument>
    {
        public async ValueTask BeforePublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            script.Entered.TrySetResult();
            await script.Release.Task;
        }
    }

    // Marks the scope as a save's command is about to run, after the save passed the guard's first check.
    private sealed class MarksAsItSaves(Script script, HookScopeGuard guard) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Mark(eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Mark(eventData);
            return ValueTask.FromResult(result);
        }

        private void Mark(CommandEventData eventData)
        {
            if (script.MarkAsItSaves && eventData.CommandSource == CommandSource.SaveChanges)
            {
                guard.MarkLeftRunning("the test's hook");
            }
        }
    }

    // Holds the left hook's read once the database has run it, its reader open on the connection, until the test lets it go.
    private sealed class HeldInside(Script script) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(InsideTag, StringComparison.Ordinal))
            {
                script.Entered.TrySetResult();
                await script.Release.Task;
            }

            return result;
        }
    }

    // What the pipeline logs, by event id.
    private sealed class HookLogs : ILoggerProvider, ILogger
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
