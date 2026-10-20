using FluentCMS.Core.Results;

namespace FluentCMS.Core.Models;

public enum ModelCallOutcome
{
    Succeeded,

    // The provider answered with an error, the call timed out or it was cancelled; it may still have been charged, so it keeps the
    // cost reserved for it.
    Failed,

    // Recorded before the call is sent, at the most it can cost, so a call is on record and counted against the spend cap even if
    // nothing about it is heard again.
    Pending,
}

// One call to a model, kept with its verbatim prompt and response (MDL-04, CHK-05, AUD-04). The prompt and response are cleared
// once the call is older than the retention; the rest of the record stays, since spend caps and the audit log count on it.
public sealed record ModelCall
{
    public const int MaxNameLength = 128;

    public required Guid Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string Profile { get; init; }

    public required string Provider { get; init; }

    // The model the profile asked for.
    public required string Model { get; init; }

    // The model the provider says answered, often with its version, such as "claude-sonnet-4-5-20250929"; null when none answered.
    public string? ModelVersion { get; init; }

    // What the call was for, such as "check:tone".
    public required string Purpose { get; init; }

    // Who caused the call, as the audit log names initiators.
    public string? InitiatorKind { get; init; }

    public Guid? InitiatorId { get; init; }

    // The agent whose spend cap the call counted against.
    public Guid? AgentCredentialId { get; init; }

    // The SHA-256 of the prompt, which audit events carry as promptHash.
    public required string PromptHash { get; init; }

    public string? Prompt { get; init; }

    public string? Response { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    // In US dollars, from the profile's prices.
    public decimal Cost { get; init; }

    public required TimeSpan Duration { get; init; }

    public required ModelCallOutcome Outcome { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset? PurgedAt { get; init; }
}

// The prompt and response store (AUD-04).
public interface IModelCallLog
{
    // Writes in a transaction of its own, so a call whose caller rolled back is still on record and still counts against the spend
    // cap. A model call is never made inside a unit of work, which would hold the database while a provider answers: this throws there.
    Task AppendAsync(ModelCall call, CancellationToken cancellationToken = default);

    // Replaces a pending call's record with what happened, in a transaction of its own.
    Task CompleteAsync(ModelCall call, CancellationToken cancellationToken = default);

    // The cost of the agent's calls since the given time (SEC-04).
    Task<decimal> SpendAsync(Guid agentCredentialId, DateTimeOffset since, CancellationToken cancellationToken = default);

    // The calls an audit event's promptHash names, so its prompt can be read back (CHK-05).
    Task<IReadOnlyList<ModelCall>> FindByPromptHashAsync(string promptHash, CancellationToken cancellationToken = default);

    // Clears the prompt, response and error of up to a batch of calls older than the retention and appends one audit event per call
    // (AUD-N2). A person purges, or the retention engine, which runs it daily; the result is how many calls were purged.
    Task<Result<int>> PurgeAsync(CancellationToken cancellationToken = default);
}

// Keeps the daily retention purge of stored prompts scheduled (AUD-04); a host calls it as it starts.
public interface IRetentionSchedule
{
    Task EnsureScheduledAsync(CancellationToken cancellationToken = default);
}
