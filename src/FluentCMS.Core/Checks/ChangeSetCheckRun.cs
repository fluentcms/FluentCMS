using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Checks;

// Where a check run ran: on the submitting request for a small set whose checks are all deterministic, or as a job (ADR-0010).
public enum CheckRunMode
{
    Inline,
    Job,
}

// The results of one item of a set: the version its operation proposes, the content type it belongs to, and each active check's
// verdict on it.
public sealed record ItemChecks(ItemKind ItemKind, Guid ItemId, int Version, string ContentType, IReadOnlyList<CheckResult> Results);

// One run of the checks on a change set, stored with the set (CHK-03): its results with their evidence, the overall status and what
// its model calls cost. Problems say what kept the engine from checking an item, or the set, and fail the run. A set reopened and
// submitted again runs again, so runs are numbered from 1.
public sealed record ChangeSetCheckRun(
    Guid Id,
    Guid ChangeSetId,
    int Number,
    CheckRunMode Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    CheckStatus Status,
    decimal Cost,
    IReadOnlyList<ItemChecks> Items,
    IReadOnlyList<string> Problems);

// A submitted set, with its check run when it ran inline and the job that runs or backs it.
public sealed record ChangeSetSubmission(ChangeSet ChangeSet, ChangeSetCheckRun? Run, Job Job);

// A set's latest check run, null before one finishes, and its latest check job, which shows a run's progress.
public sealed record ChangeSetChecksState(ChangeSet ChangeSet, ChangeSetCheckRun? Run, Job? Job);

// The check runs of change sets, which Infrastructure stores.
public interface ICheckRuns
{
    // Inside the caller's unit of work, so the run commits with the move it decides; the run comes back with its number.
    Task<ChangeSetCheckRun> RecordAsync(ChangeSetCheckRun run, CancellationToken cancellationToken = default);

    Task<ChangeSetCheckRun?> LatestAsync(Guid changeSetId, CancellationToken cancellationToken = default);
}

// Checks run on every change set before routing (CHK-03). A set is submitted through here, so the checks are sure to run on it.
public interface IChangeSetChecks
{
    // Moves an open set to checks running and enqueues its check run with it. A small set whose active checks are all deterministic
    // is then checked at once (PERF-02); its job only waits in case that run did not finish. Anything else runs as the job.
    Task<Result<ChangeSetSubmission>> SubmitAsync(Guid changeSetId, CancellationToken cancellationToken = default);

    // Runs the active checks on every entry and entity the set proposes, stores the results and moves the set: to checks failed if
    // any check fails, and to awaiting review otherwise. Made as the check engine (ADR-0012) for the submission whose job is named;
    // a set no longer in checks running for it is left as it is. Progress gets the items done and the total, and false from it
    // stops the run.
    Task<Result<ChangeSetCheckRun?>> RunAsync(
        Guid changeSetId,
        Guid jobId,
        CheckRunMode mode,
        Func<int, int, Task<bool>>? progress = null,
        CancellationToken cancellationToken = default);

    // Fails the set with a problem when its checks cannot run at all, so it does not stay in checks running for good.
    Task<Result<ChangeSetCheckRun?>> AbandonAsync(Guid changeSetId, Guid jobId, CheckRunMode mode, CancellationToken cancellationToken = default);

    Task<Result<ChangeSetChecksState>> GetAsync(Guid changeSetId, CancellationToken cancellationToken = default);
}
