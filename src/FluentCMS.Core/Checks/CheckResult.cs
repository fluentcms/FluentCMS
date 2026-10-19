namespace FluentCMS.Core.Checks;

// What a check's verdict means for the content (CHK-03): a failure blocks or warns as the check's severity says.
public enum CheckStatus
{
    Pass,
    Warn,
    Fail,
    NeedsReview,
}

// One active check's verdict on one piece of content, with its evidence and, for an LLM-judged check, the model call it rests on.
public sealed record CheckResult(
    Guid CheckId,
    string Check,
    int Version,
    string Type,
    CheckKind Kind,
    CheckSeverity Severity,
    string Description,
    CheckStatus Status,
    IReadOnlyList<CheckFinding> Findings,
    CheckModelUse? Model)
{
    public static CheckStatus StatusOf(CheckVerdict verdict, CheckSeverity severity)
    {
        return verdict switch
        {
            CheckVerdict.Pass => CheckStatus.Pass,
            CheckVerdict.Fail => severity == CheckSeverity.Warn ? CheckStatus.Warn : CheckStatus.Fail,
            _ => CheckStatus.NeedsReview,
        };
    }

    // The status of a set of results: any failure fails it, then anything a person must decide, then any warning.
    public static CheckStatus Overall(IEnumerable<CheckResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var statuses = results.Select(result => result.Status).ToHashSet();
        return statuses.Contains(CheckStatus.Fail) ? CheckStatus.Fail
            : statuses.Contains(CheckStatus.NeedsReview) ? CheckStatus.NeedsReview
            : statuses.Contains(CheckStatus.Warn) ? CheckStatus.Warn
            : CheckStatus.Pass;
    }
}

public enum CheckSelection
{
    All,

    // Only the deterministic checks, which cost nothing and answer at once (ADR-0010).
    Deterministic,
}

// The results of the checks that ran, and the names of the active checks that applied but were left out by the selection, so
// a pass never hides a check that did not run.
public sealed record CheckRun(IReadOnlyList<CheckResult> Results, IReadOnlyList<string> Skipped);

// Runs the active checks that apply to a piece of content (CHK-03, CHK-04). A model call is never made inside a unit of work, so
// neither is a run that includes LLM-judged checks: the caller runs it before any unit of work begins.
public interface ICheckRunner
{
    // ChargedTo names the agent whose spend cap LLM-judged checks count against, when the caller is not that agent (SEC-04).
    Task<CheckRun> RunAsync(
        CheckSubject subject,
        CheckSelection selection = CheckSelection.All,
        Guid? chargedTo = null,
        CancellationToken cancellationToken = default);

    // Runs the given active checks, read once by a caller that checks many subjects, so each is judged by the same checks.
    Task<CheckRun> RunAsync(
        CheckSubject subject,
        IReadOnlyList<Check> active,
        CheckSelection selection = CheckSelection.All,
        Guid? chargedTo = null,
        CancellationToken cancellationToken = default);
}
