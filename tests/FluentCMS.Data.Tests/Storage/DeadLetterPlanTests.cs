using System.Data.Common;
using System.Text;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Events;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

// The dead-letter lists page newest first over rows that are kept for weeks, so each walks an index of its own rows in the
// whole order of the list rather than sorting every job or every outbox row.
public sealed class DeadLetterPlanTests : IDisposable
{
    private readonly JobFixture _fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _fixture.Dispose();

    [SqliteFact("SQLite's query plans")]
    public async Task TheListOfDeadLetteredJobsWalksTheIndexOnStatusAndCompletion()
    {
        var plans = new PlanCollector("FROM \"cms_job\"");
        using var instance = _fixture.NewInstance(cms: cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(plans)));

        await instance.GetRequiredService<IJobDeadLetters>().ListAsync(50, 50, Token);

        var plan = plans.Plans.ShouldHaveSingleItem();
        plan.ShouldContain("ix_cms_job_status_completed");
        plan.ShouldNotContain("TEMP B-TREE");
    }

    [SqliteFact("SQLite's query plans")]
    public async Task TheListOfFailedDeliveriesWalksTheFailedIndex()
    {
        var plans = new PlanCollector("FROM \"cms_outbox\"");
        using var instance = _fixture.NewInstance(cms: cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(plans)));

        await instance.GetRequiredService<IEventDeadLetters>().ListAsync(50, 50, Token);

        var plan = plans.Plans.ShouldHaveSingleItem();
        plan.ShouldContain("ix_cms_outbox_failed");
        plan.ShouldNotContain("ix_cms_outbox_failed_");
        plan.ShouldNotContain("TEMP B-TREE");
    }

    // The drain's read of one handler's candidates seeks that handler's pending rows in event order and never walks another
    // handler's backlog.
    [SqliteFact("SQLite's query plans")]
    public void TheClaimOfOneHandlerSeeksItsPendingRowsInEventOrder()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var drainer = _fixture.Services.GetRequiredService<OutboxDrainer>();

        var plan = QueryPlanTests.Plan(drainer.CandidatesOf(context, "webhooks.content.published", DateTimeOffset.UtcNow, [Guid.CreateVersion7()]));

        plan.ShouldContain("ix_cms_outbox_handler_pending (handler=?)");
        plan.ShouldNotContain("TEMP B-TREE");
    }

    // The prune of failed rows seeks those that failed before its time, so a burst of recent failures within the retention is
    // never read, and passes over the old rows it keeps by their attempts in the index. Planned over a large failed set, with
    // the statistics of ANALYZE.
    [SqliteFact("SQLite's query plans")]
    public void ThePruneOfFailedRowsSeeksThoseThatFailedBeforeItsTime()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var now = DateTimeOffset.UtcNow;
        AddFailedRows(context, now);

        var plan = QueryPlanTests.Plan(OutboxPruneJob.Batch(context, OutboxPruneJob.FailedBefore(now - TimeSpan.FromDays(30), 8), 500));

        plan.ShouldContain("ix_cms_outbox_failed_available_at (available_at<?)");
        plan.ShouldNotContain("TEMP B-TREE");
    }

    // The purge of one handler's failed rows seeks that handler's, rather than read every failed row for them.
    [SqliteFact("SQLite's query plans")]
    public void ThePurgeOfAHandlersFailedRowsSeeksThatHandler()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        AddFailedRows(context, DateTimeOffset.UtcNow);

        var plan = QueryPlanTests.Plan(OutboxPruneJob.Batch(context, OutboxPruneJob.FailedOf("acme.old"), 500));

        plan.ShouldContain("ix_cms_outbox_failed_handler (handler=?)");
        plan.ShouldNotContain("TEMP B-TREE");
    }

    // A receiver down for days: thousands of recent failures of one handler, a few hundred that ran out of attempts long ago,
    // and old rows owed to a plugin that is gone, which the prune keeps.
    private static void AddFailedRows(SystemDbContext context, DateTimeOffset now)
    {
        OutboxMessage Failed(string handler, DateTimeOffset failedAt, int attempts, long sequence) => new()
        {
            EventId = Guid.CreateVersion7(),
            EventType = "Acme.Thing",
            Handler = handler,
            Payload = "{}",
            OccurredAt = failedAt,
            Sequence = sequence,
            Status = OutboxStatus.Failed,
            Attempts = attempts,
            AvailableAt = failedAt,
            LastError = "Down.",
        };

        var sequence = 0L;
        context.AddRange(Enumerable.Range(0, 3000).Select(index => Failed("webhooks.receiver", now - TimeSpan.FromMinutes(index), 8, ++sequence)));
        context.AddRange(Enumerable.Range(0, 300).Select(index => Failed($"acme.handler{index % 5}", now - TimeSpan.FromDays(40 + (index % 20)), 8, ++sequence)));
        context.AddRange(Enumerable.Range(0, 200).Select(index => Failed("acme.old", now - TimeSpan.FromDays(60), 0, ++sequence)));
        context.SaveChanges();
        context.Database.ExecuteSqlRaw("ANALYZE");
    }

    // Asks SQLite how it runs each list query, with the parameters the list gave it.
    private sealed class PlanCollector(string table) : DbCommandInterceptor
    {
        public List<string> Plans { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(table, StringComparison.Ordinal) && command.CommandText.Contains("ORDER BY", StringComparison.Ordinal))
            {
                using var explain = command.Connection!.CreateCommand();
                explain.Transaction = command.Transaction;
#pragma warning disable CA2100 // The text of a command EF built.
                explain.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
#pragma warning restore CA2100
                foreach (DbParameter parameter in command.Parameters)
                {
                    var copy = explain.CreateParameter();
                    copy.ParameterName = parameter.ParameterName;
                    copy.Value = parameter.Value;
                    explain.Parameters.Add(copy);
                }

                using var reader = explain.ExecuteReader();
                var plan = new StringBuilder();
                while (reader.Read())
                {
                    plan.AppendLine(reader.GetString(3));
                }

                lock (Plans)
                {
                    Plans.Add(plan.ToString());
                }
            }

            return ValueTask.FromResult(result);
        }
    }
}
