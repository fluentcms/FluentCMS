using FluentCMS.Core.Checks;

namespace FluentCMS.Core.Rules;

// Where proposing checks for one version of a rule stands (RUL-04).
public enum RuleCompileStatus
{
    // A job is asking the model for checks.
    Compiling,

    // Checks were proposed; the author confirms, edits or rejects each.
    Proposed,

    // No check was proposed, and the message says why. The rule is guidance until one of its checks is confirmed (MDL-03).
    Advisory,
}

public enum RuleProposalState
{
    Proposed,
    Confirmed,
    Rejected,

    // Still undecided when checks were proposed for the version again; kept so a number always names the same proposal.
    Replaced,
}

// A check the system proposed for a version of a rule, numbered within the rule. Once confirmed, CheckName is the check it became.
public sealed record RuleCheckProposal(int Number, int RuleVersion, string CheckName, CheckDefinition Definition, RuleProposalState State);

// The compile of one version and the checks it proposed, in number order.
public sealed record RuleCompilation(
    string Slug,
    int Version,
    RuleCompileStatus Status,
    string? Message,
    Guid? JobId,
    IReadOnlyList<RuleCheckProposal> Proposals);

// What a compile found, which the check engine records for the version it compiled.
public sealed record RuleCompileOutcome(RuleCompileStatus Status, string? Message, IReadOnlyList<CheckDefinition> Proposals)
{
    // The model and prompt the proposals came from, which the audit event cites (MDL-04).
    public string? Model { get; init; }

    public string? PromptHash { get; init; }

    public static RuleCompileOutcome Advisory(string message)
    {
        return new RuleCompileOutcome(RuleCompileStatus.Advisory, message, []);
    }
}
