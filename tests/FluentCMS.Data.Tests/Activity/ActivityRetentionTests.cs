using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Jobs;
using FluentCMS.Core.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Activity.ActivityFixture;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Activity;

public sealed class ActivityRetentionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Task<bool> RunAsync(ServiceDatabase database) =>
        database.AsAsync(Principal.System, async services =>
        {
            var operation = new OperationContext(Principal.System, Origin.Job, SiteA, "en", null, "retention", Now);
            await services.GetRequiredService<ActivityRetentionJob>().ExecuteAsync(
                new ActivityRetentionPayload(),
                new JobContext(Guid.CreateVersion7(), ActivityRetentionJob.Type, "default", 1, 3, operation, Token));
            return true;
        }, origin: Origin.Job);

    private static List<string> Kept(ServiceDatabase database) =>
        [.. database.ActivityRows().Select(row => row.Action).Order()];

    [Fact]
    public async Task RowsPastTheDefaultAgeOfThirtyDaysAreRemoved()
    {
        using var database = new ServiceDatabase(ActivityFixture.On());
        database.Add(
            Row("old", SiteA.Value, Now.AddDays(-31)),
            Row("edge", SiteA.Value, Now.AddDays(-29)),
            Row("new", SiteA.Value, Now.AddMinutes(-5)),
            Row("old-instance", null, Now.AddDays(-45)),
            Row("new-instance", null, Now.AddDays(-1)));

        await RunAsync(database);

        Kept(database).ShouldBe(["edge", "new", "new-instance"]);
    }

    [Fact]
    public async Task ASiteWithItsOwnAgeIsPrunedByThatAgeAndTheOthersByTheDefault()
    {
        using var database = new ServiceDatabase(ActivityFixture.On(cms =>
        {
            cms.Activity.Retention = TimeSpan.FromDays(30);
            cms.Activity.SiteRetention["B"] = TimeSpan.FromDays(5);
        }));
        database.Add(
            Row("a-10d", SiteA.Value, Now.AddDays(-10)),
            Row("a-40d", SiteA.Value, Now.AddDays(-40)),
            Row("b-3d", SiteB.Value, Now.AddDays(-3)),
            Row("b-10d", SiteB.Value, Now.AddDays(-10)),
            Row("instance-10d", null, Now.AddDays(-10)),
            Row("instance-40d", null, Now.AddDays(-40)));

        await RunAsync(database);

        Kept(database).ShouldBe(["a-10d", "b-3d", "instance-10d"]);
    }

    [Fact]
    public async Task ASiteMayKeepItsRowsLongerThanTheDefault()
    {
        using var database = new ServiceDatabase(ActivityFixture.On(cms =>
        {
            cms.Activity.Retention = TimeSpan.FromDays(7);
            cms.Activity.SiteRetention["a"] = TimeSpan.FromDays(90);
        }));
        database.Add(
            Row("a-60d", SiteA.Value, Now.AddDays(-60)),
            Row("a-100d", SiteA.Value, Now.AddDays(-100)),
            Row("b-60d", SiteB.Value, Now.AddDays(-60)));

        await RunAsync(database);

        Kept(database).ShouldBe(["a-60d"]);
    }

    [Fact]
    public async Task AnAgeForASiteThatDoesNotExistChangesNothing()
    {
        using var database = new ServiceDatabase(ActivityFixture.On(cms => cms.Activity.SiteRetention["nowhere"] = TimeSpan.FromDays(1)));
        database.Add(Row("old", SiteA.Value, Now.AddDays(-31)), Row("recent", SiteA.Value, Now.AddDays(-2)));

        await RunAsync(database);

        Kept(database).ShouldBe(["recent"]);
    }

    [Fact]
    public async Task ABacklogLargerThanABatchIsRemovedInSeveral()
    {
        using var database = new ServiceDatabase(ActivityFixture.On(cms => cms.Activity.PruneBatchSize = 2));
        database.Add([.. Enumerable.Range(0, 7).Select(index => Row($"old-{index}", SiteA.Value, Now.AddDays(-40 - index))), Row("new", SiteA.Value, Now)]);

        await RunAsync(database);

        Kept(database).ShouldBe(["new"]);
    }

    [Fact]
    public async Task TheScheduleIsStoredAtStartupAndOnceHoweverOftenTheHostStarts()
    {
        using var database = new ServiceDatabase(ActivityFixture.On(cms => cms.Activity.PruneSchedule = "0 4 * * *"));
        var service = database.Database.Services.GetServices<IHostedService>().OfType<ActivityScheduleService>().Single();

        await service.StartAsync(Token);
        await service.StopAsync(Token);
        await service.StartAsync(Token);
        await service.StopAsync(Token);

        using var scope = database.Database.NewScope();
        var recurring = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().AsNoTracking().Where(job => job.Name == "activity.retention").ToList();
        var job = recurring.ShouldHaveSingleItem();
        job.Type.ShouldBe("activity.retention");
        job.Cron.ShouldBe("0 4 * * *");
    }

    [Fact]
    public async Task TurningTheLogOffSwitchesTheStoredScheduleOffAndStartingOnAgainNeverSwitchesItOn()
    {
        using var database = new ServiceDatabase(ActivityFixture.On());
        await database.Database.Services.GetServices<IHostedService>().OfType<ActivityScheduleService>().Single().StartAsync(Token);
        Recurring(database).Enabled.ShouldBeTrue();

        using (var off = database.Database.NewInstance())
        {
            await off.GetServices<IHostedService>().OfType<ActivityScheduleService>().Single().StartAsync(Token);
        }

        Recurring(database).Enabled.ShouldBeFalse();

        using var on = database.Database.NewInstance(ActivityFixture.On());
        await on.GetServices<IHostedService>().OfType<ActivityScheduleService>().Single().StartAsync(Token);

        Recurring(database).Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task AScheduleAnOperatorSwitchedOffStaysOffWhenTheHostStarts()
    {
        using var database = new ServiceDatabase(ActivityFixture.On());
        await database.Database.Services.GetServices<IHostedService>().OfType<ActivityScheduleService>().Single().StartAsync(Token);
        database.Database.Services.GetRequiredService<SystemDbContext>().Set<RecurringJob>().Where(job => job.Name == "activity.retention").ExecuteUpdate(set => set.SetProperty(job => job.Enabled, false));

        using var restarted = database.Database.NewInstance(ActivityFixture.On());
        await restarted.GetServices<IHostedService>().OfType<ActivityScheduleService>().Single().StartAsync(Token);

        Recurring(database).Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task AJobThatRunsAfterTheLogWasTurnedOffRemovesNothing()
    {
        using var database = new ServiceDatabase();
        database.Add(Row("old", SiteA.Value, Now.AddDays(-40)));

        await RunAsync(database);

        Kept(database).ShouldBe(["old"]);
    }

    [Fact]
    public async Task EveryBatchOfThePruneIsAnIndexSearchWithNoSort()
    {
        var plans = new List<string>();
        using var database = new ServiceDatabase(ActivityFixture.On(cms =>
        {
            cms.Activity.SiteRetention["b"] = TimeSpan.FromDays(5);
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(new PlanCollector(plans)));
        }));
        database.Add(Row("a-old", SiteA.Value, Now.AddDays(-40)), Row("b-old", SiteB.Value, Now.AddDays(-10)), Row("instance-old", null, Now.AddDays(-40)));

        await RunAsync(database);

        Kept(database).ShouldBeEmpty();
        plans.Count.ShouldBeGreaterThanOrEqualTo(2);
        plans.ShouldAllBe(plan => !plan.Contains("TEMP B-TREE", StringComparison.Ordinal));
        plans.ShouldContain(plan => plan.Contains("ix_cms_activity_occurred", StringComparison.Ordinal));
        plans.ShouldContain(plan => plan.Contains("ix_cms_activity_site_occurred", StringComparison.Ordinal));
        plans.ShouldAllBe(plan => plan.Contains("USING INDEX", StringComparison.Ordinal) || plan.Contains("USING COVERING INDEX", StringComparison.Ordinal));
    }

    private static RecurringJob Recurring(ServiceDatabase database)
    {
        using var scope = database.Database.NewScope();
        return scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().AsNoTracking().Single(job => job.Name == "activity.retention");
    }

    // Asks SQLite how it runs each batch the job selects, with the parameters the job gave it.
    private sealed class PlanCollector(List<string> plans) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Collect(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Collect(command);
            return ValueTask.FromResult(result);
        }

        private void Collect(DbCommand command)
        {
            if (Regex.IsMatch(command.CommandText, @"^SELECT ""c"".""id""\s+FROM"))
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

                lock (plans)
                {
                    plans.Add(plan.ToString());
                }
            }
        }
    }
}
