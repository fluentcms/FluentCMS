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

// Runs the active checks that apply to a piece of content (CHK-03, CHK-04). A model call is never made inside a unit of work, so
// neither is a run that includes LLM-judged checks.
public interface ICheckRunner
{
    Task<IReadOnlyList<CheckResult>> RunAsync(
        CheckSubject subject,
        CheckSelection selection = CheckSelection.All,
        CancellationToken cancellationToken = default);
}
