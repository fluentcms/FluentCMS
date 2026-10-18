namespace FluentCMS.Core.Agents;

// What a credential has already done, counted for its caps (SEC-04).
public interface IAgentActivity
{
    Task<int> ChangeSetsOpenedSinceAsync(Guid credentialId, DateTimeOffset since, CancellationToken cancellationToken = default);
}
