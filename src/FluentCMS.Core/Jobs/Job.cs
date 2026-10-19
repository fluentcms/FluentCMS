using System.Text.Json;
using FluentCMS.Core.Identity;

namespace FluentCMS.Core.Jobs;

// Background work kept in the database (ADR-0010): what to do, for whom, and how far it got, which tools and the desk read.
public sealed record Job
{
    public required Guid Id { get; init; }

    // The handler that runs it (IJobHandler.Type).
    public required string Type { get; init; }

    public required JsonElement Payload { get; init; }

    // What the job works on, such as a change set, so its progress can be found from that item.
    public JobSubject? Subject { get; init; }

    // Who enqueued it; the handler runs as them, so what it changes names them (AUD-01). Null for the system's own work.
    public Initiator? Initiator { get; init; }

    public required JobState State { get; init; }

    public required int Attempts { get; init; }

    public required int MaxAttempts { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    // When it is due; a retry moves it later.
    public required DateTimeOffset ScheduledAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public JobProgress Progress { get; init; } = JobProgress.None;

    public string? LastError { get; init; }
}

public sealed record JobSubject(string Type, Guid Id);

// Done out of Total when the total is known; the message says what the job is doing, for whoever reads it.
public sealed record JobProgress(int Done, int? Total, string? Message)
{
    public static JobProgress None { get; } = new(0, null, null);
}

public enum JobState
{
    Queued,
    Running,
    Succeeded,

    // The handler returned a failure, which another attempt would not change.
    Failed,

    // Every attempt threw, or no handler knows the job's type.
    Dead,
}
