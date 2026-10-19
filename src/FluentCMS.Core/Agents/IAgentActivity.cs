namespace FluentCMS.Core.Agents;

// What a credential has already done, counted for its caps (SEC-04).
public interface IAgentActivity
{
    Task<int> ChangeSetsOpenedSinceAsync(Guid credentialId, DateTimeOffset since, CancellationToken cancellationToken = default);

    // Calls a refusal stopped are left out, so an agent over its cap is not kept there by its own retries; running ones count.
    Task<int> ToolCallsSinceAsync(Guid credentialId, DateTimeOffset since, CancellationToken cancellationToken = default);

    // Holds the credential until the current unit of work ends, so concurrent capped actions of one agent are counted one at a time.
    // Called first in the unit of work, before the count it protects.
    Task HoldAsync(Guid credentialId, CancellationToken cancellationToken = default);

    // Adds the call to the current unit of work, so it is recorded in the same transaction as the count that let it run.
    Task StartToolCallAsync(ToolCall call, CancellationToken cancellationToken = default);
}
