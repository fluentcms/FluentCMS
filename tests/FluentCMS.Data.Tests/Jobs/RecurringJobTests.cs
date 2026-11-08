using FluentCMS.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Jobs;

public sealed class RecurringJobTests : IDisposable
{
    private const string EveryFiveMinutes = "*/5 * * * *";
    private const string Name = "sweep-things";

    private readonly JobFixture _fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _fixture.Dispose();

    private Task AddAsync(string cron = EveryFiveMinutes, string name = Name) =>
        _fixture.Scheduler.AddRecurringAsync<ProbeJob, ProbePayload>(name, cron, new ProbePayload(name), queue: null, cancellationToken: Token);

    private Task FireAsync() => _fixture.Services.GetRequiredService<RecurringJobs>().FireDueAsync(Token);

    [Fact]
    public async Task AJobFiresOnScheduleAndRuns()
    {
        await AddAsync();

        var stored = _fixture.Recurring(Name);
        stored.Enabled.ShouldBeTrue();
        stored.Type.ShouldBe(JobFixture.ProbeType);
        stored.Queue.ShouldBe(JobRunnerOptions.DefaultQueue);
        stored.NextRunAt.ShouldBe(JobFixture.Start.AddMinutes(5));

        _fixture.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        await FireAsync();
        _fixture.Jobs().ShouldBeEmpty();

        _fixture.Clock.Advance(TimeSpan.FromTicks(1));
        await FireAsync();

        var job = _fixture.Jobs().ShouldHaveSingleItem();
        job.Type.ShouldBe(JobFixture.ProbeType);
        job.RunAt.ShouldBe(JobFixture.Start.AddMinutes(5));
        job.Origin.ShouldBe(Origin.Job);
        _fixture.Recurring(Name).NextRunAt.ShouldBe(JobFixture.Start.AddMinutes(10));

        await FireAsync();
        _fixture.Jobs().Count.ShouldBe(1);

        (await _fixture.Executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "worker", Token)).ShouldBeTrue();
        _fixture.Probe.RunsOf(Name).ShouldBe(1);

        _fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        await FireAsync();
        _fixture.Jobs().Count.ShouldBe(2);
    }

    [Fact]
    public async Task RunsMissedWhileNothingWasUpCollapseIntoOne()
    {
        await AddAsync();

        _fixture.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2));
        await FireAsync();

        _fixture.Jobs().ShouldHaveSingleItem();
        _fixture.Recurring(Name).NextRunAt.ShouldBe(JobFixture.Start.AddHours(1).AddMinutes(5));
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("wal")]
    public async Task AnOccurrenceFiresOnceAcrossTwoRunners(string journalMode)
    {
        _fixture.Database.Execute($"PRAGMA journal_mode = {journalMode}");
        await AddAsync();

        var instances = new[] { _fixture.NewInstance(), _fixture.NewInstance() }
            .Select(instance => instance.GetRequiredService<RecurringJobs>()).ToList();

        const int Occurrences = 20;
        for (var occurrence = 1; occurrence <= Occurrences; occurrence++)
        {
            _fixture.Clock.Advance(TimeSpan.FromMinutes(5));
            using var start = new ManualResetEventSlim();

            var firing = instances.Select(recurring => Task.Run(async () =>
            {
                start.Wait(Token);
                await recurring.FireDueAsync(Token);
            }, Token)).ToArray();
            start.Set();
            await Task.WhenAll(firing);

            _fixture.Jobs().Count.ShouldBe(occurrence);
        }

        _fixture.Jobs().Select(job => job.RunAt).ShouldBeUnique();
    }

    [Theory]
    [InlineData("not a cron")]
    [InlineData("* * * *")]
    [InlineData("* * * * * *")]
    [InlineData("99 * * * *")]
    [InlineData("0 0 30 2 *")]
    public async Task ABadCronIsRefusedWhenAdded(string cron)
    {
        var failure = await Should.ThrowAsync<ArgumentException>(() => AddAsync(cron));

        failure.ParamName.ShouldBe("cron");
        failure.Message.ShouldContain("FCMS2503");

        using var scope = _fixture.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().AnyAsync(Token)).ShouldBeFalse();
    }

    [Fact]
    public async Task AddingAgainWithTheSameCronKeepsTheNextRun()
    {
        await AddAsync();

        _fixture.Clock.Advance(TimeSpan.FromMinutes(7));
        await AddAsync();

        _fixture.Recurring(Name).NextRunAt.ShouldBe(JobFixture.Start.AddMinutes(5), "a restart must not skip the occurrence it was down for");
    }

    [Fact]
    public async Task AddingAgainWithAnotherCronMovesTheNextRun()
    {
        await AddAsync();

        await AddAsync("0 * * * *");

        var stored = _fixture.Recurring(Name);
        stored.Cron.ShouldBe("0 * * * *");
        stored.NextRunAt.ShouldBe(JobFixture.Start.AddHours(1));
    }

    [Fact]
    public async Task InstancesAddingAtTheSameStartupAgree()
    {
        var schedulers = Enumerable.Range(0, 4)
            .Select(_ => _fixture.NewInstance().GetRequiredService<FluentCMS.Jobs.IJobScheduler>());

        await Task.WhenAll(schedulers.Select(scheduler => Task.Run(
            () => scheduler.AddRecurringAsync<ProbeJob, ProbePayload>(Name, EveryFiveMinutes, new ProbePayload(Name), queue: null, cancellationToken: Token),
            Token)));

        _fixture.Recurring(Name).Cron.ShouldBe(EveryFiveMinutes);
    }

    [Fact]
    public async Task ADisabledJobDoesNotFire()
    {
        await AddAsync();
        using (var scope = _fixture.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>()
                .ExecuteUpdateAsync(job => job.SetProperty(row => row.Enabled, false), Token);
        }

        _fixture.Clock.Advance(TimeSpan.FromHours(1));
        await FireAsync();

        _fixture.Jobs().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARowEditedToABadCronDoesNotStopTheOthers()
    {
        await AddAsync(name: "broken");
        await AddAsync(name: "fine");
        using (var scope = _fixture.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>()
                .Where(job => job.Name == "broken")
                .ExecuteUpdateAsync(job => job.SetProperty(row => row.Cron, "nonsense"), Token);
        }

        _fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        await FireAsync();

        _fixture.Jobs().ShouldHaveSingleItem().Payload.ShouldContain("fine");
    }
}
