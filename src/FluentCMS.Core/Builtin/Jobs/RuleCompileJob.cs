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

        var outcome = await context.Services.GetRequiredService<RuleCompiler>().ProposeAsync(compiling.Value, context.Job.Initiator, cancellationToken);
        return Replaced((await rules.RecordAsync(ruleId, version, context.Job.Id, outcome, cancellationToken)).Errors);
    }

    // A compile that a later request replaced has nothing left to do.
    private static Result Replaced(IReadOnlyList<Error> errors)
    {
        return errors.Count == 0 || errors.All(error => error.Code == RuleErrorCodes.CompileReplaced) ? Result.Success() : Result.Failure(errors);
    }
}
