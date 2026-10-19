using FluentCMS.Core.Agents;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class CallsPerPeriodTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_call_past_the_cap_is_refused_until_the_period_moves_on_and_refused_calls_do_not_count(string provider)
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(
            install,
            settings: database.Configuration,
            configure: builder => builder.Services.AddSingleton<TimeProvider>(clock).AddSingleton(McpAgents.WhoAmI));
        var twoAnHour = McpAgents.Credential(clock.Now) with
        {
            Caps = AgentCaps.Create(2, TimeSpan.FromHours(1), 5m, TimeSpan.FromDays(1), 200, 20).Value,
        };
        var issued = await McpAgents.IssueAsync(host, await host.CreateFirstAdministratorAsync(), twoAnHour);
        await using var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);

        Assert.NotEqual(true, (await client.CallToolAsync("whoami")).IsError);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.NotEqual(true, (await client.CallToolAsync("whoami")).IsError);

        // Refused calls are left out of the count, so the second refusal still counts two calls, not three.
        foreach (var _ in Enumerable.Range(0, 2))
        {
            clock.Now = clock.Now.AddSeconds(1);
            var refused = await client.CallToolAsync("whoami");
            Assert.True(refused.IsError);
            Assert.Contains(
                "has made 2 tool calls in the last hour, and its cap is 2 (calls per period)",
                Assert.IsType<TextContentBlock>(Assert.Single(refused.Content)).Text,
                StringComparison.Ordinal);
        }

        await using (var scope = host.App.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
            var calls = (await context.ToolCalls.AsNoTracking().ToListAsync()).OrderBy(call => call.Timestamp).ThenBy(call => call.Id);
            Assert.Equal(
                [ToolCallOutcome.Succeeded, ToolCallOutcome.Succeeded, ToolCallOutcome.Refused, ToolCallOutcome.Refused],
                calls.Select(call => call.Outcome));

            var exceeded = await context.AuditEvents.AsNoTracking().Where(row => row.Operation == "agent.calls_per_period_exceeded").ToListAsync();
            Assert.Equal(2, exceeded.Count);
            Assert.All(exceeded, row =>
            {
                Assert.Equal(issued.Credential.Id, row.SubjectId);
                Assert.Equal(issued.Credential.Id, row.AgentCredentialId);
            });
        }

        // An hour after the first call only the second one is still within the period.
        clock.Now = clock.Now.AddMinutes(59).AddSeconds(1);
        Assert.NotEqual(true, (await client.CallToolAsync("whoami")).IsError);
        Assert.True((await client.CallToolAsync("whoami")).IsError);
    }
}
