using FluentCMS.Core.Agents;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Agents.AgentCredentialScenario;

namespace FluentCMS.IntegrationTests.Agents;

public sealed class ToolCallLogTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_call_reads_back_as_it_was_appended(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);
        var agent = (await scenario.IssueAsync()).Credential;
        var call = Call(agent) with { Model = "example-model-1", TaskId = "TASK-42", Outcome = ToolCallOutcome.Failed };

        await scenario.Services.GetRequiredService<IToolCallLog>().AppendAsync(call);

        var row = Assert.Single(await scenario.QueryAsync(context => context.ToolCalls.AsNoTracking().ToListAsync()));
        Assert.Equal(call.Id, row.Id);
        Assert.Equal(Start, row.Timestamp);
        Assert.Equal(agent.Id, row.AgentCredentialId);
        Assert.Equal("catalog-sync", row.AgentName);
        Assert.Equal("whoami", row.Tool);
        Assert.Equal("example-model-1", row.Model);
        Assert.Equal("TASK-42", row.TaskId);
        Assert.Equal(ToolCallOutcome.Failed, row.Outcome);
        Assert.Equal(TimeSpan.FromMilliseconds(12), row.Duration);
    }

    private static ToolCall Call(AgentCredential agent)
    {
        return new ToolCall
        {
            Id = Guid.CreateVersion7(Start),
            Timestamp = Start,
            AgentCredentialId = agent.Id,
            AgentName = agent.DisplayName,
            Tool = "whoami",
            Outcome = ToolCallOutcome.Succeeded,
            Duration = TimeSpan.FromMilliseconds(12),
        };
    }
}
