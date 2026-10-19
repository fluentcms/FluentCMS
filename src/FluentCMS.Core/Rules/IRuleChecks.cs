using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Rules;

// The checks proposed for a rule, and the ones its author confirmed (RUL-04). Saving a version starts its compile; the author then
// confirms, edits or rejects each proposal. Only a person decides, since a confirmed check guards content (D17), and only the check
// engine records what a compile proposed. Every write appends its audit event in the same unit of work.
public interface IRuleChecks
{
    Task<Result<RuleCompilation>> GetAsync(string slug, int version, CancellationToken cancellationToken = default);

    // Proposes checks for the version again, such as once a model is configured. The proposals not yet decided are marked replaced,
    // and the new ones are numbered after every earlier one.
    Task<Result<RuleCompilation>> CompileAsync(string slug, int version, CancellationToken cancellationToken = default);

    // Creates the proposed check, or the definition the author edited it to, under its proposed name or checkName, and activates it
    // once its examples get their verdicts, in one unit of work. The check guards content only while the rule is active at the
    // version it was proposed for.
    Task<Result<RuleCheckProposal>> ConfirmAsync(
        string slug,
        int number,
        CheckDefinition? definition = null,
        string? checkName = null,
        CancellationToken cancellationToken = default);

    Task<Result<RuleCheckProposal>> RejectAsync(string slug, int number, CancellationToken cancellationToken = default);

    // For the compile job: the version's definition while the job is still that version's compile.
    Task<Result<RuleDefinition>> CompilingAsync(Guid ruleId, int version, Guid jobId, CancellationToken cancellationToken = default);

    // Stores what the job's compile found. A compile that a later request replaced records nothing and gets rule_compile_replaced.
    Task<Result> RecordAsync(Guid ruleId, int version, Guid jobId, RuleCompileOutcome outcome, CancellationToken cancellationToken = default);
}
