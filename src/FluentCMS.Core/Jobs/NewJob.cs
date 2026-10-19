using System.Text.Json;

namespace FluentCMS.Core.Jobs;

public sealed record NewJob(string Type, JsonElement Payload)
{
    public const int DefaultMaxAttempts = 3;

    public JobSubject? Subject { get; init; }

    // Null to run as soon as a worker is free.
    public DateTimeOffset? RunAt { get; init; }

    public int MaxAttempts { get; init; } = DefaultMaxAttempts;
}
