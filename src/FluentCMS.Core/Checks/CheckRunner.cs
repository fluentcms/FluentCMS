namespace FluentCMS.Core.Checks;

// Each check sees the content its scope names, at the version that is active, in the order of their names. The services are this
// scope's, so a check type that asks a model is charged to the caller.
internal sealed class CheckRunner(ICheckDirectory checks, CheckTypeRegistry checkTypes, IServiceProvider services) : ICheckRunner
{
    public async Task<IReadOnlyList<CheckResult>> RunAsync(
        CheckSubject subject,
        CheckSelection selection = CheckSelection.All,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var results = new List<CheckResult>();
        foreach (var check in await checks.ListActiveAsync(cancellationToken))
        {
            var definition = check.Definition;
            var kind = checkTypes.Find(definition.Type)?.Kind ?? CheckKind.Deterministic;
            if (!definition.Scope.AppliesTo(subject.ContentType) || (selection == CheckSelection.Deterministic && kind != CheckKind.Deterministic))
            {
                continue;
            }

            var outcome = await checkTypes.EvaluateAsync(
                definition.Type,
                new CheckInput(definition.Settings, definition.Scope.Narrow(subject), services),
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

        return results;
    }
}
