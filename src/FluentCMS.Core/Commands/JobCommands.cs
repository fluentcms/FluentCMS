using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Jobs;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.Core.Commands.AdminCommandSupport;

namespace FluentCMS.Core.Commands;

internal sealed class JobsListCommand : HostCommand
{
    public const string Queue = "--queue";
    public const string Status = "--status";
    public const int Newest = 100;

    public override string Name => "jobs list";

    public override string Description => $"Lists the newest {Newest} jobs, the newest first; a payload is never shown.";

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Queue, "Only the jobs of this queue.", "name"),
        new(Status, $"Only the jobs with this status: {string.Join(", ", JobAdministration.Statuses)}.", "status"),
        JsonOption,
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var status = context.Arguments.Value(Status);
        if (status is not null && !JobAdministration.Statuses.Contains(status, StringComparer.Ordinal))
        {
            throw new CommandException(CommandErrorCodes.InvalidValue, $"{Status} is one of {string.Join(", ", JobAdministration.Statuses)}", CommandExitCodes.BadUsage);
        }

        var jobs = await context.Services.GetRequiredService<JobAdministration>().ListAsync(new JobFilter(status, null, context.Arguments.Value(Queue)), 1, Newest, cancellationToken);
        if (context.Arguments.Has(Json))
        {
            await context.Output.WriteLineAsync(ToJson(new ItemPage<JobInfo>(jobs.Items, jobs.Total, jobs.Page, jobs.PageSize)));
            return CommandExitCodes.Success;
        }

        await WriteTableAsync(
            context.Output,
            ["ID", "TYPE", "QUEUE", "STATUS", "ATTEMPTS", "RUN AT", "LAST ERROR"],
            jobs.Items.Select(job => (IReadOnlyList<string>)
                [job.Id.ToString("D"), job.Type, job.Queue, job.Status, $"{job.Attempts}/{job.MaxAttempts}", Moment(job.RunAt), Short(job.LastError)]));
        if (jobs.Total > jobs.Items.Count)
        {
            await context.Error.WriteLineAsync($"The newest {jobs.Items.Count} of {jobs.Total} jobs are shown.");
        }

        return CommandExitCodes.Success;
    }
}

// Queues a run of a recurring job for the instances that work its queue, or with --inline runs it here, for an instance
// that is not running. The name is a recurring job's, which holds the payload of its runs.
internal sealed class JobsRunCommand : HostCommand
{
    public const string Inline = "--inline";

    public override string Name => "jobs run";

    public override string Description => "Queues a run of a recurring job now, or with --inline runs it to its end in this process.";

    public override IReadOnlyList<string> Arguments { get; } = ["name"];

    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new(Inline, "Runs the job here with one attempt rather than queuing it; a failure is dead-lettered."),
    ];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var name = context.Arguments.Positional[0];
        var inline = context.Arguments.Has(Inline);
        var jobs = context.Services.GetRequiredService<JobAdministration>();
        if (await jobs.RunRecurringAsync(name, inline, cancellationToken) is not { Job: var job })
        {
            var names = await jobs.RecurringNamesAsync(cancellationToken);
            throw new CommandException(
                CommandErrorCodes.UnknownJob,
                names.Count == 0
                    ? $"there is no recurring job '{name}'; an instance adds its recurring jobs when it first starts, and this one has none yet"
                    : $"there is no recurring job '{name}'; the recurring jobs are {string.Join(", ", names)}",
                CommandExitCodes.BadUsage);
        }

        if (!inline)
        {
            await context.Output.WriteLineAsync($"Queued {job.Type} as the job {job.Id:D} on the queue {job.Queue}; an instance that works the queue runs it.");
            return CommandExitCodes.Success;
        }

        if (job.Status == JobStatus.Succeeded)
        {
            await context.Output.WriteLineAsync($"Ran {job.Type} as the job {job.Id:D}; it succeeded.");
            return CommandExitCodes.Success;
        }

        throw new CommandException(
            CommandErrorCodes.InlineJobFailed,
            job.Status == JobStatus.DeadLettered
                ? $"the job {job.Id:D} ({job.Type}) failed and is dead-lettered: {Short(job.LastError, 400)}"
                : $"the job {job.Id:D} ({job.Type}) did not finish and is {job.Status}; an instance that works the queue {job.Queue} runs it");
    }
}

internal sealed class JobsRetryCommand : HostCommand
{
    public override string Name => "jobs retry";

    public override string Description => "Queues a dead-lettered job again, with the runner's default number of attempts.";

    public override IReadOnlyList<string> Arguments { get; } = ["id"];

    public override async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var id = Id(context.Arguments.Positional[0], "id");
        var job = (await context.Services.GetRequiredService<JobAdministration>().RetryAsync(id, cancellationToken)).Job;
        await context.Output.WriteLineAsync($"Queued the job {job.Id:D} ({job.Type}) again on the queue {job.Queue}.");
        return CommandExitCodes.Success;
    }
}
