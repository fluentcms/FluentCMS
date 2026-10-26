using System.Text.Json;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Results;
using FluentCMS.Core.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Jobs;

// Proposes checks for a saved version of a rule (RUL-04), as the check engine on behalf of the person who saved it (ADR-0012). The
// payload only names the version; whether this job is still its compile, and what the rule says, come from what is stored.
internal sealed class RuleCompileJob : IJobHandler
{
    private const string Abandoned =
        "Checks could not be proposed after every attempt failed, so the rule is advisory until checks are proposed again.";

    public string Type => RuleCompiler.JobType;

    public SystemInitiator RunAs => SystemInitiator.Checks;

    public async Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
    {
        var payload = context.Job.Payload;
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("ruleId", out var id)
            || id.ValueKind != JsonValueKind.String
            || !id.TryGetGuid(out var ruleId)
            || !payload.TryGetProperty("version", out var number)
            || number.ValueKind != JsonValueKind.Number
            || !number.TryGetInt32(out var version))
        {
            return Result.Failure("payload_invalid", "A rule compile names the rule as ruleId and its version as version.");
        }

        var rules = context.Services.GetRequiredService<IRuleChecks>();
        var compiling = await rules.CompilingAsync(ruleId, version, context.Job.Id, cancellationToken);
        if (!compiling.IsSuccess)
        {
            return Replaced(compiling.Errors);
        }

        try
        {
            var outcome = await context.Services.GetRequiredService<RuleCompiler>().ProposeAsync(compiling.Value, context.Job.Initiator, cancellationToken);
            return Replaced((await rules.RecordAsync(ruleId, version, context.Job.Id, outcome, cancellationToken)).Errors);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && context.Job.Attempts >= context.Job.MaxAttempts)
        {
            // The last attempt leaves the version advisory, so it does not stay compiling for good; proposing again starts over.
            var abandoned = await rules.RecordAsync(ruleId, version, context.Job.Id, RuleCompileOutcome.Advisory(Abandoned), cancellationToken);
            return Result.Failure(abandoned.IsSuccess ? [new Error("rule_compile_abandoned", exception.Message)] : abandoned.Errors);
        }
    }

    // A compile that a later request replaced has nothing left to do.
    private static Result Replaced(IReadOnlyList<Error> errors)
    {
        return errors.Count == 0 || errors.All(error => error.Code == RuleErrorCodes.CompileReplaced) ? Result.Success() : Result.Failure(errors);
    }
}
