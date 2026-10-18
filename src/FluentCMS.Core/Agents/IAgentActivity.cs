namespace FluentCMS.Core.Agents;

// What a credential has already done, counted for its caps (SEC-04).
public interface IAgentActivity
{
    Task<int> ChangeSetsOpenedSinceAsync(Guid credentialId, DateTimeOffset since, CancellationToken cancellationToken = default);

    // Calls a refusal stopped are left out, so an agent over its cap is not kept there by its own retries.
    Task<int> ToolCallsSinceAsync(Guid credentialId, DateTimeOffset since, CancellationToken cancellationToken = default);
}
