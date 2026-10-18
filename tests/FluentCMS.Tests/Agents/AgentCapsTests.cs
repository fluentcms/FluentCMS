using FluentCMS.Core.Agents;

namespace FluentCMS.Tests.Agents;

public sealed class AgentCapsTests
{
    [Fact]
    public void Caps_keep_their_values()
    {
        var caps = AgentCaps.Create(600, TimeSpan.FromHours(1), 5.25m, TimeSpan.FromDays(30), 200, 20).Value;

        Assert.Equal(600, caps.CallsPerPeriod);
        Assert.Equal(TimeSpan.FromHours(1), caps.CallPeriod);
        Assert.Equal(5.25m, caps.LlmSpendPerPeriod);
        Assert.Equal(TimeSpan.FromDays(30), caps.LlmSpendPeriod);
        Assert.Equal(200, caps.ItemsPerChangeSet);
        Assert.Equal(20, caps.ChangeSetsPerDay);
    }

    [Fact]
    public void Zero_spend_is_a_cap_that_allows_no_model_calls()
    {
        Assert.True(AgentCaps.Create(1, TimeSpan.FromSeconds(1), 0m, TimeSpan.FromSeconds(1), 1, 1).IsSuccess);
    }

    [Fact]
    public void Every_problem_with_the_caps_is_reported_at_once()
    {
        var result = AgentCaps.Create(0, TimeSpan.Zero, -0.01m, TimeSpan.FromMilliseconds(1500), -1, 0);

        Assert.Equal(6, result.Errors.Count);
        Assert.All(result.Errors, error => Assert.Equal(AgentCredentialErrorCodes.CapInvalid, error.Code));
        Assert.StartsWith("Calls per period", result.Errors[0].Message, StringComparison.Ordinal);
        Assert.StartsWith("The call period", result.Errors[1].Message, StringComparison.Ordinal);
        Assert.StartsWith("LLM spend per period", result.Errors[2].Message, StringComparison.Ordinal);
        Assert.StartsWith("The LLM spend period", result.Errors[3].Message, StringComparison.Ordinal);
        Assert.StartsWith("Items per change set", result.Errors[4].Message, StringComparison.Ordinal);
        Assert.StartsWith("Change sets per day", result.Errors[5].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Caps_with_the_same_values_are_equal()
    {
        Assert.Equal(TestAgentCredentials.Caps(), TestAgentCredentials.Caps());
        Assert.Equal(TestAgentCredentials.Caps(), AgentCaps.Restore(600, TimeSpan.FromHours(1), 5.250m, TimeSpan.FromDays(1), 200, 20));
    }
}
