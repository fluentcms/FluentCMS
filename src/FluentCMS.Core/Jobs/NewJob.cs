using System.Text.Json;
using FluentCMS.Core.Identity;

namespace FluentCMS.Core.Jobs;

public sealed record NewJob(string Type, JsonElement Payload)
{
    public const int DefaultMaxAttempts = 3;

    public JobSubject? Subject { get; init; }

    // Null to run as soon as a worker is free.
    public DateTimeOffset? RunAt { get; init; }

    public int MaxAttempts { get; init; } = DefaultMaxAttempts;

    // The engine the job runs as instead of whoever enqueues it, for work no person or agent does, such as the check run a
    // submission starts (ADR-0012). Null runs the job as whoever enqueues it.
    public SystemInitiator? RunAs { get; init; }
}
