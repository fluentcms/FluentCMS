namespace FluentCMS.Core.Checks;

public enum CheckVerdict
{
    Pass,
    Fail,

    // The type could not decide, for example because no model is configured (MDL-03); a person looks instead.
    NeedsReview,
}

// The evidence for a verdict. The message says what to change (UX-03); the pointer is a JSON Pointer into the subject's fields,
// such as /body or /gallery/0/caption, and empty for the subject as a whole; the excerpt is the text that was found, if any.
public sealed record CheckFinding(string Message, string Pointer = "", string? Excerpt = null);

public sealed class CheckOutcome
{
    private CheckOutcome(CheckVerdict verdict, IReadOnlyList<CheckFinding> findings)
    {
        Verdict = verdict;
        Findings = findings;
    }

    public CheckVerdict Verdict { get; }

    public IReadOnlyList<CheckFinding> Findings { get; }

    // The model call an LLM-judged outcome rests on, so the result names its model, prompt and cost (CHK-05); null for every other.
    public CheckModelUse? Model { get; private init; }

    public static CheckOutcome Pass()
    {
        return new CheckOutcome(CheckVerdict.Pass, []);
    }

    // A failure always says what to change, so it has at least one finding.
    public static CheckOutcome Fail(IEnumerable<CheckFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var list = findings.ToList();
        if (list.Count == 0 || list.Any(finding => finding is null))
        {
            throw new ArgumentException("A failed check has at least one finding, and none is null.", nameof(findings));
        }

        return new CheckOutcome(CheckVerdict.Fail, list);
    }

    public static CheckOutcome Fail(string message, string pointer = "", string? excerpt = null)
    {
        return Fail([new CheckFinding(message, pointer, excerpt)]);
    }

    public static CheckOutcome NeedsReview(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new CheckOutcome(CheckVerdict.NeedsReview, [new CheckFinding(reason)]);
    }

    public CheckOutcome WithModel(CheckModelUse model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new CheckOutcome(Verdict, Findings) { Model = model };
    }
}

// Which model call judged the content: the record that keeps its verbatim prompt and response, the model and version that answered,
// the prompt's hash that audit events carry, and what the call cost (MDL-04, CHK-05).
public sealed record CheckModelUse(Guid CallId, string Provider, string Model, string PromptHash, decimal Cost);
