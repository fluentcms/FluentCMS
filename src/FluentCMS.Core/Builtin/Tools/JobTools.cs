using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Results;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// How far background work an agent started has got (MCP-08). An agent reads its own jobs only.
public sealed class JobTools : IToolProvider
{
    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "get_job",
            "Get a job's progress",
            "Returns one of your background jobs, such as the one bulk_update_entries started: its state (queued, running, "
            + "succeeded, failed or dead), its progress as done of total with a message, its attempts and its errors. A failed job "
            + "lists what failed; a dead one stopped on an error another attempt did not fix. Call it again until the state is "
            + "succeeded, failed or dead.",
            ToolSchemas.Object(ToolSchemas.Uuid("id", "The job's id, as the call that started it returned it.")),
            readOnly: true,
            GetAsync),
    ];

    private static async Task<ToolResult> GetAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var job = await call.Services.GetRequiredService<IJobQueue>().GetAsync(id!.Value, cancellationToken);
        var caller = call.Services.GetRequiredService<ICurrentInitiator>().Current;

        // Another caller's job reads as missing, so its existence says nothing about someone else's work.
        return job.IsSuccess && job.Value.Initiator == caller
            ? ToolResult.Success(ToolJson.JobRecord(job.Value))
            : ToolResult.Failure(
                [new Error(JobErrorCodes.JobNotFound, $"You have no job {id}.")],
                "A job's id comes back from the call that started it, such as bulk_update_entries.");
    }
}
