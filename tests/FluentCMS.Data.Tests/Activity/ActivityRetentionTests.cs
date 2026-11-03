using FluentCMS.Content;
using FluentCMS.Core.ActivityLog;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
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
        using var database = new ServiceDatabase();
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
        using var database = new ServiceDatabase(cms =>
        {
            cms.Activity.Retention = TimeSpan.FromDays(30);
            cms.Activity.SiteRetention["B"] = TimeSpan.FromDays(5);
        });
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
        using var database = new ServiceDatabase(cms =>
        {
            cms.Activity.Retention = TimeSpan.FromDays(7);
            cms.Activity.SiteRetention["a"] = TimeSpan.FromDays(90);
        });
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
        using var database = new ServiceDatabase(cms => cms.Activity.SiteRetention["nowhere"] = TimeSpan.FromDays(1));
        database.Add(Row("old", SiteA.Value, Now.AddDays(-31)), Row("recent", SiteA.Value, Now.AddDays(-2)));

        await RunAsync(database);

        Kept(database).ShouldBe(["recent"]);
    }

    [Fact]
    public async Task ABacklogLargerThanABatchIsRemovedInSeveral()
    {
        using var database = new ServiceDatabase(cms => cms.Activity.PruneBatchSize = 2);
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
    public void WithTheLogTurnedOffNothingIsScheduled()
    {
        using var database = new ServiceDatabase();

        database.Database.Services.GetServices<IHostedService>().OfType<ActivityScheduleService>().ShouldBeEmpty();
    }
}
