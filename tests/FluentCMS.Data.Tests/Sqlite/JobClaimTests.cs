using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sqlite;

public sealed class JobClaimTests : IDisposable
{
    private const string Queue = "default";

    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LeaseUntil = Now.AddMinutes(5);
    private static readonly string[] Workers = ["worker-1", "worker-2"];

    private readonly SqliteFileDatabase _database = new();
    private readonly ServiceProvider _services;

    public JobClaimTests()
    {
        _services = _database.CreateServices();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.Migrate();
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    private object? Read(Guid job, string column)
    {
        using var connection = new SqliteConnection(_database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM cms_job WHERE id = $id";
        command.Parameters.AddWithValue("$id", job.ToString().ToUpperInvariant());
        return command.ExecuteScalar();
    }

    private Guid AddJob(
        string queue = Queue,
        string status = JobClaimStatus.Pending,
        DateTimeOffset? runAt = null,
        int priority = 0,
        int attempts = 0,
        int maxAttempts = 3,
        DateTimeOffset? leaseUntil = null,
        string? leaseOwner = null)
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var job = new Job
        {
            Type = "test",
            Payload = "{}",
            Queue = queue,
            RunAt = runAt ?? Now.AddMinutes(-1),
            Priority = priority,
            Status = status,
            Attempts = attempts,
            MaxAttempts = maxAttempts,
            LeaseUntil = leaseUntil,
            LeaseOwner = leaseOwner,
            Origin = Origin.Job,
        };
        context.Add(job);
        context.SaveChanges();

        return job.Id;
    }

    private async Task<Guid?> ClaimAsync(string owner = "worker", string queue = Queue)
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var dialect = scope.ServiceProvider.GetRequiredService<IJobClaimDialect>();
        return await dialect.ClaimAsync(context.Database, new JobClaim(queue, owner, Now, LeaseUntil), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ClaimingTakesTheLeaseAndCountsTheAttempt()
    {
        var job = AddJob();

        (await ClaimAsync("worker-1")).ShouldBe(job);

        Read(job, "status").ShouldBe(JobClaimStatus.Running);
        Read(job, "lease_owner").ShouldBe("worker-1");
        Read(job, "lease_until").ShouldBe(LeaseUntil.UtcTicks);
        Read(job, "attempts").ShouldBe(1L);
    }

    [Fact]
    public async Task NothingToClaimGivesNull() => (await ClaimAsync()).ShouldBeNull();

    [Fact]
    public async Task AClaimedJobIsNotClaimedAgain()
    {
        var job = AddJob();

        (await ClaimAsync("worker-1")).ShouldBe(job);
        (await ClaimAsync("worker-2")).ShouldBeNull();

        Read(job, "lease_owner").ShouldBe("worker-1");
    }

    [Fact]
    public async Task OnlyAJobThatIsDueOnTheQueueAndStillHasAttemptsIsClaimed()
    {
        AddJob(runAt: Now.AddSeconds(1));
        AddJob(queue: "other");
        AddJob(attempts: 3, maxAttempts: 3);
        AddJob(status: "succeeded");
        AddJob(status: JobClaimStatus.Running, leaseUntil: LeaseUntil, leaseOwner: "worker-0");

        (await ClaimAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task AnExpiredLeaseIsClaimedAgain()
    {
        var job = AddJob(status: JobClaimStatus.Running, attempts: 1, leaseUntil: Now.AddTicks(-1), leaseOwner: "dead-worker");

        (await ClaimAsync("worker-1")).ShouldBe(job);

        Read(job, "lease_owner").ShouldBe("worker-1");
        Read(job, "attempts").ShouldBe(2L);
    }

    [Fact]
    public async Task TheHighestPriorityGoesFirstThenTheEarliestRunAt()
    {
        var later = AddJob(runAt: Now.AddMinutes(-1));
        var earlier = AddJob(runAt: Now.AddMinutes(-2));
        var urgent = AddJob(runAt: Now.AddMinutes(-1), priority: 10);

        (await ClaimAsync()).ShouldBe(urgent);
        (await ClaimAsync()).ShouldBe(earlier);
        (await ClaimAsync()).ShouldBe(later);
    }

    // EF creates a database in WAL mode; one created by other means keeps the rollback journal.
    [Theory]
    [InlineData("delete")]
    [InlineData("wal")]
    public async Task OfTwoConcurrentClaimersOnlyOneWins(string journalMode)
    {
        _database.Execute($"PRAGMA journal_mode = {journalMode}");

        for (var round = 0; round < 25; round++)
        {
            var queue = $"round-{round}";
            var job = AddJob(queue);
            using var start = new ManualResetEventSlim();

            var claimers = Workers.Select(owner => Task.Run(async () =>
            {
                start.Wait(TestContext.Current.CancellationToken);
                return (Owner: owner, Job: await ClaimAsync(owner, queue));
            }, TestContext.Current.CancellationToken)).ToArray();
            start.Set();

            var results = await Task.WhenAll(claimers);

            var winner = results.Where(result => result.Job is not null).ShouldHaveSingleItem();
            winner.Job.ShouldBe(job);
            Read(job, "lease_owner").ShouldBe(winner.Owner);
            Read(job, "attempts").ShouldBe(1L);
        }
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("wal")]
    public async Task ConcurrentClaimersGetEachJobOnce(string journalMode)
    {
        _database.Execute($"PRAGMA journal_mode = {journalMode}");

        var jobs = Enumerable.Range(0, 20).Select(_ => AddJob()).ToList();

        var claimers = Enumerable.Range(0, 8).Select(worker => Task.Run(async () =>
        {
            var claimed = new List<Guid>();
            while (await ClaimAsync($"worker-{worker}") is { } job)
            {
                claimed.Add(job);
            }

            return claimed;
        }, TestContext.Current.CancellationToken)).ToArray();

        var claimed = (await Task.WhenAll(claimers)).SelectMany(list => list).ToList();

        claimed.ShouldBe(jobs, ignoreOrder: true);
    }
}
