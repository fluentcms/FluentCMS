using System.Text.Json;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;

namespace FluentCMS.Infrastructure.Jobs;

// A Job in the Jobs table, with the lease a worker holds on it.
public sealed class JobRow
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = "{}";

    public string? SubjectType { get; set; }

    public Guid? SubjectId { get; set; }

    public string? InitiatorKind { get; set; }

    public Guid? InitiatorId { get; set; }

    public string? InitiatorName { get; set; }

    public JobState State { get; set; }

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ScheduledAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseUntil { get; set; }

    public int ProgressDone { get; set; }

    public int? ProgressTotal { get; set; }

    public string? ProgressMessage { get; set; }

    public string? LastError { get; set; }

    // Every claim moves it on and applies only to the revision it read, so of two workers claiming one job, one gets it.
    public int Revision { get; set; }

    internal Job ToJob()
    {
        using var payload = JsonDocument.Parse(Payload);

        return new Job
        {
            Id = Id,
            Type = Type,
            Payload = payload.RootElement.Clone(),
            Subject = SubjectType is { } subjectType && SubjectId is { } subjectId ? new JobSubject(subjectType, subjectId) : null,
            Initiator = InitiatorKind switch
            {
                InitiatorKinds.User => new UserInitiator(InitiatorId!.Value, InitiatorName!),
                InitiatorKinds.Agent => new AgentInitiator(InitiatorId!.Value, InitiatorName!),
                InitiatorKinds.System => InitiatorKinds.RestoreSystem(InitiatorName!, InitiatorId!.Value),
                null => null,
                _ => throw new InvalidOperationException($"Job {Id} names an unknown initiator kind '{InitiatorKind}'."),
            },
            State = State,
            Attempts = Attempts,
            MaxAttempts = MaxAttempts,
            CreatedAt = CreatedAt,
            ScheduledAt = ScheduledAt,
            StartedAt = StartedAt,
            FinishedAt = FinishedAt,
            Progress = new JobProgress(ProgressDone, ProgressTotal, ProgressMessage),
            LastError = LastError,
        };
    }
}
