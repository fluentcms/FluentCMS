using FluentCMS.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sqlite;

// cms_job arrives with #2698, so this is a scratch table with the columns of spec section 4.12.
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
        Execute("""
            CREATE TABLE cms_job (
                id TEXT NOT NULL PRIMARY KEY,
                type TEXT NOT NULL,
                payload TEXT NOT NULL CHECK (json_valid(payload)),
                queue TEXT NOT NULL,
                site_id TEXT NULL,
                run_at INTEGER NOT NULL,
                priority INTEGER NOT NULL,
                status TEXT NOT NULL,
                attempts INTEGER NOT NULL,
                max_attempts INTEGER NOT NULL,
                lease_until INTEGER NULL,
                lease_owner TEXT NULL,
                last_error TEXT NULL,
                created_at INTEGER NOT NULL,
                created_by TEXT NULL,
                origin TEXT NOT NULL,
                completed_at INTEGER NULL)
            """);
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    private static string StoredId(Guid id) => id.ToString().ToUpperInvariant();

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = new SqliteConnection(_database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private object? Read(Guid job, string column)
    {
        using var connection = new SqliteConnection(_database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM cms_job WHERE id = $id";
        command.Parameters.AddWithValue("$id", StoredId(job));
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
        var id = Guid.CreateVersion7();
        Execute(
            """
            INSERT INTO cms_job (id, type, payload, queue, run_at, priority, status, attempts, max_attempts, lease_until, lease_owner, created_at, origin)
            VALUES ($id, 'test', '{}', $queue, $runAt, $priority, $status, $attempts, $maxAttempts, $leaseUntil, $leaseOwner, 0, 'Job')
            """,
            ("$id", StoredId(id)),
            ("$queue", queue),
            ("$runAt", (runAt ?? Now.AddMinutes(-1)).UtcTicks),
            ("$priority", priority),
            ("$status", status),
            ("$attempts", attempts),
            ("$maxAttempts", maxAttempts),
            ("$leaseUntil", leaseUntil?.UtcTicks),
            ("$leaseOwner", leaseOwner));

        return id;
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
        Execute($"PRAGMA journal_mode = {journalMode}");

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
        Execute($"PRAGMA journal_mode = {journalMode}");

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
