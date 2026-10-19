using FluentCMS.Core.Identity;

namespace FluentCMS.Core.Checks;

// Each check sees the content its scope names, at the version that is active, in the order of their names. The services are this
// scope's, so a check type that asks a model is charged to the caller.
internal sealed class CheckRunner(ICheckDirectory checks, CheckTypeRegistry checkTypes, IServiceProvider services) : ICheckRunner
{
    public async Task<CheckRun> RunAsync(
        CheckSubject subject,
        CheckSelection selection = CheckSelection.All,
        Initiator? onBehalfOf = null,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(subject, await checks.ListActiveAsync(cancellationToken), selection, onBehalfOf, cancellationToken);
    }

    public async Task<CheckRun> RunAsync(
        CheckSubject subject,
        IReadOnlyList<Check> active,
        CheckSelection selection = CheckSelection.All,
        Initiator? onBehalfOf = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(active);

        var results = new List<CheckResult>();
        var skipped = new List<string>();
        foreach (var check in active)
        {
            var definition = check.Definition;
            var kind = checkTypes.Find(definition.Type)?.Kind ?? CheckKind.Deterministic;
            if (!definition.Scope.AppliesTo(subject.ContentType))
            {
                continue;
            }

            if (selection == CheckSelection.Deterministic && kind != CheckKind.Deterministic)
            {
                skipped.Add(check.Name);
                continue;
            }

            var outcome = await checkTypes.EvaluateAsync(
                definition.Type,
                new CheckInput(definition.Settings, definition.Scope.Narrow(subject), services, onBehalfOf),
                cancellationToken);

            results.Add(new CheckResult(
                check.Id,
                check.Name,
                check.Version,
                definition.Type,
                kind,
                definition.Severity,
                definition.Description,
                CheckResult.StatusOf(outcome.Verdict, definition.Severity),
                outcome.Findings,
                outcome.Model));
        }

        return new CheckRun(results, skipped);
    }
}
