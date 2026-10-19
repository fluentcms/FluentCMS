using System.Text.Json;
using FluentCMS.Core;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Checks;

// The check run job runs as the check engine, retries what another attempt can fix and, on its last attempt, fails the set rather
// than leave it in checks running for good (CHK-03, ADR-0012).
public sealed class CheckRunJobTests : IDisposable
{
    private static readonly Guid SetId = Guid.NewGuid();

    private readonly FakeChangeSetChecks _checks = new();
    private readonly ServiceProvider _provider;

    public CheckRunJobTests()
    {
        _provider = new ServiceCollection().AddFluentCmsCore().AddSingleton<IChangeSetChecks>(_checks).BuildServiceProvider();
    }

    private IJobHandler Handler => _provider.GetServices<IJobHandler>().Single(handler => handler.Type == "checks.run");

    [Fact]
    public async Task It_runs_as_the_check_engine_for_its_own_submission_and_reports_progress()
    {
        var job = Job(attempts: 1);
        var reported = new List<JobProgress>();

        var result = await Handler.RunAsync(Context(job, reported), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SystemInitiator.Checks, Handler.RunAs);
        Assert.Equal((SetId, job.Id, CheckRunMode.Job), _checks.Ran.Single());
        Assert.Equal("Checked 1 of 2 items.", Assert.Single(reported).Message);
    }

    [Fact]
    public async Task A_payload_that_names_no_set_fails_the_job()
    {
        var result = await Handler.RunAsync(Context(Job(attempts: 1, payload: """{"changeSetId": "not a guid"}""")), CancellationToken.None);

        Assert.Equal("payload_invalid", Assert.Single(result.Errors).Code);
        Assert.Empty(_checks.Ran);
    }

    [Fact]
    public async Task A_write_that_met_the_run_is_retried()
    {
        _checks.Run = () => Result<ChangeSetCheckRun?>.Failure(ChangeSetErrorCodes.Conflict, "The set changed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler.RunAsync(Context(Job(attempts: 1)), CancellationToken.None));
        Assert.Empty(_checks.Abandoned);
    }

    [Fact]
    public async Task A_fault_before_the_last_attempt_is_retried_and_on_the_last_one_fails_the_set()
    {
        _checks.Run = () => throw new InvalidOperationException("The database is unreachable.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler.RunAsync(Context(Job(attempts: 5)), CancellationToken.None));
        var last = Job(attempts: 6);
        var result = await Handler.RunAsync(Context(last), CancellationToken.None);

        Assert.Equal(("check_run_abandoned", "The database is unreachable."), (Assert.Single(result.Errors).Code, result.Errors[0].Message));
        Assert.Equal((SetId, last.Id), _checks.Abandoned.Single());
    }

    [Fact]
    public async Task A_lost_lease_ends_the_attempt_without_failing_the_set()
    {
        _checks.Run = () => Result<ChangeSetCheckRun?>.Failure("check_run_lease_lost", "Another worker has the job.");

        var result = await Handler.RunAsync(Context(Job(attempts: 6)), CancellationToken.None);

        Assert.Equal("check_run_lease_lost", Assert.Single(result.Errors).Code);
        Assert.Empty(_checks.Abandoned);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static Job Job(int attempts, string? payload = null)
    {
        return new Job
        {
            Id = Guid.CreateVersion7(),
            Type = "checks.run",
            Payload = JsonSerializer.Deserialize<JsonElement>(payload ?? JsonSerializer.Serialize(new { changeSetId = SetId })),
            State = JobState.Running,
            Attempts = attempts,
            MaxAttempts = 6,
            CreatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow,
        };
    }

    private JobContext Context(Job job, List<JobProgress>? reported = null)
    {
        return new JobContext(job, _provider, (progress, _) =>
        {
            reported?.Add(progress);
            return Task.FromResult(true);
        });
    }

    private sealed class FakeChangeSetChecks : IChangeSetChecks
    {
        public Func<Result<ChangeSetCheckRun?>> Run { get; set; } = () => Result<ChangeSetCheckRun?>.Success(null);

        public List<(Guid ChangeSetId, Guid JobId, CheckRunMode Mode)> Ran { get; } = [];

        public List<(Guid ChangeSetId, Guid JobId)> Abandoned { get; } = [];

        public async Task<Result<ChangeSetCheckRun?>> RunAsync(
            Guid changeSetId,
            Guid jobId,
            CheckRunMode mode,
            Func<int, int, Task<bool>>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Ran.Add((changeSetId, jobId, mode));
            if (progress is not null)
            {
                await progress(1, 2);
            }

            return Run();
        }

        public Task<Result<ChangeSetCheckRun?>> AbandonAsync(Guid changeSetId, Guid jobId, CheckRunMode mode, CancellationToken cancellationToken = default)
        {
            Abandoned.Add((changeSetId, jobId));
            return Task.FromResult(Result<ChangeSetCheckRun?>.Success(null));
        }

        public Task<Result<ChangeSetSubmission>> SubmitAsync(Guid changeSetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ChangeSetChecksState>> GetAsync(Guid changeSetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
