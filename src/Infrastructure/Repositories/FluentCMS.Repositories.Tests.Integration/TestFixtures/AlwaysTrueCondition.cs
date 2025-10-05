using FluentCMS.Repositories.DataInitialization.Abstractions;

namespace FluentCMS.Repositories.Tests.Integration.TestFixtures;

public class AlwaysTrueCondition : IDbInitializationCondition
{
    public string Name => "Always True Condition";
    public Task<bool> ShouldExecute(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
