using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Jobs;

// The check run of a submitted change set (CHK-03), as the check engine whoever submitted the set (ADR-0012). A run is safe to
// repeat: a set the checks have already moved on is left as it is. The payload only names the set; whether to run, and where the
// set goes, come from what is stored.
internal sealed class CheckRunJob : IJobHandler
{
    public string Type => ChangeSetChecks.JobType;

    public SystemInitiator RunAs => SystemInitiator.Checks;

    public async Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
    {
        var payload = context.Job.Payload;
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("changeSetId", out var id)
            || id.ValueKind != JsonValueKind.String
            || !id.TryGetGuid(out var changeSetId))
        {
            return Result.Failure("payload_invalid", "A check run names the change set it checks as changeSetId.");
        }

        var checks = context.Services.GetRequiredService<IChangeSetChecks>();
        try
        {
            var ran = await checks.RunAsync(
                changeSetId,
                context.Job.Id,
                CheckRunMode.Job,
                (done, total) => context.ReportProgressAsync(done, total, $"Checked {done} of {total} items.", cancellationToken),
                cancellationToken);

            // A write that met the run's own can be got past by another attempt on what is stored then.
            if (!ran.IsSuccess && ran.Errors.Any(error => error.Code == ChangeSetErrorCodes.Conflict))
            {
                throw new InvalidOperationException(ran.Errors[0].Message);
            }

            return ran.IsSuccess ? Result.Success() : Result.Failure(ran.Errors);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && context.Job.Attempts >= context.Job.MaxAttempts)
        {
            // The last attempt fails the set, so the agent can reopen it, rather than leave it in checks running for good.
            var abandoned = await checks.AbandonAsync(changeSetId, context.Job.Id, CheckRunMode.Job, cancellationToken);
            return Result.Failure(abandoned.IsSuccess ? [new Error("check_run_abandoned", exception.Message)] : abandoned.Errors);
        }
    }
}
